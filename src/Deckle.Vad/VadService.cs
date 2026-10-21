using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Core;

namespace Deckle.Vad;

// Owns the Silero VAD's lifecycle: resolve the model path, verify it, load the
// session (or kick a one-time background download), and dispose. A caller hands in
// how to resolve the models directory and then asks EnsureReady() per take; the
// loaded session is cached for the service's life.
//
// Resolution is lazy and never blocks: if the model is present it loads
// synchronously (a couple of MB, milliseconds); if absent, a one-time background
// download is started and EnsureReady returns null — the current take runs without
// the trim and a later take picks the model up.
public sealed class VadService : IDisposable
{
    private readonly Func<string> _resolveModelsDirectory;

    private SileroVad? _vad;
    private bool _downloadKicked;   // a background download is in flight or done
    private bool _loadFailed;       // constructing the session threw — don't retry

    // The open "takes run untrimmed" incident, null when none. Guarded because the
    // background download reports its failure from a pool thread.
    private readonly object _incidentLock = new();
    private string? _incidentReason;

    public VadService(Func<string> resolveModelsDirectory)
    {
        _resolveModelsDirectory = resolveModelsDirectory;
    }

    // Returns the loaded VAD, or null when the model isn't usable yet. Call from a
    // single thread (the streaming worker) — there is no concurrency on _vad.
    public SileroVad? EnsureReady()
    {
        if (_vad is not null) return _vad;
        if (_loadFailed) return null;

        string modelPath = Path.Combine(_resolveModelsDirectory(), SileroVadModel.FileName);
        bool modelPresent = File.Exists(modelPath);

        if (modelPresent)
        {
            // Hash verify and session construction run at most once per service,
            // on the first streaming take's critical path; timed together.
            var sw = Stopwatch.StartNew();
            if (FileMatchesExpectedSha(modelPath, out Exception? readError))
            {
                try
                {
                    _vad = new SileroVad(modelPath);
                    sw.Stop();
                    DeckleVadSource.Log.SpeechTrimVadLoaded();
                    DeckleVadSource.Log.SpeechTrimVadLoadedDetail(modelPath);
                    DeckleVadSource.Log.SpeechTrimVadLoadComplete(sw.ElapsedMilliseconds);
                    CloseIncident();
                    return _vad;
                }
                catch (Exception ex)
                {
                    _loadFailed = true;
                    OpenIncident("load_failed", modelPath, ex.GetType().Name, ex.Message);
                    // Bytes match the pinned build yet it won't construct — should not
                    // happen, but delete defensively so a later launch retries clean.
                    TryDeleteModel(modelPath);
                    return null;
                }
            }

            // Present but not the pinned build — an older model version (so a version
            // bump actually takes effect instead of silently keeping the old file) or
            // a corrupt copy. File.Exists alone can't tell the difference; drop it and
            // fall through to the verified download.
            if (readError is null)
                OpenIncident("checksum_mismatch", modelPath, "", "");
            else
                OpenIncident("checksum_read_failed", modelPath, readError.GetType().Name, readError.Message);
            TryDeleteModel(modelPath);
        }

        if (!_downloadKicked)
        {
            // A present model already opened the incident with its own cause.
            if (!modelPresent)
                OpenIncident("model_missing", modelPath, "", "");
            _downloadKicked = true;
            _ = DownloadModelAsync(modelPath);
        }
        return null;
    }

    private async Task DownloadModelAsync(string modelPath)
    {
        DeckleVadSource.Log.SpeechTrimVadDownloadStart();
        DeckleVadSource.Log.SpeechTrimVadDownloadStartDetail(SileroVadModel.Url);
        try
        {
            // expectedSha256 guards against a corrupt/truncated transfer: the
            // downloader verifies the bytes and discards the .partial on mismatch,
            // so a bad file is never published. CancellationToken.None is deliberate
            // — this fire-and-forget transfer (a couple of MB) outlives Dispose
            // rather than being torn down mid-flight; the OS reclaims it at exit.
            ProvisioningResult result = await SileroVadModel.ProvisionAsync(
                Path.GetDirectoryName(modelPath)!, progress: null, CancellationToken.None)
                .ConfigureAwait(false);

            if (result.Success)
            {
                DeckleVadSource.Log.SpeechTrimVadDownloadComplete();
                DeckleVadSource.Log.SpeechTrimVadDownloadCompleteDetail(modelPath);
            }
            else
            {
                // The downloader reports a failure as text, not as an exception.
                OpenIncident("download_failed", modelPath, "", result.ErrorMessage ?? "");
            }
        }
        catch (Exception ex)
        {
            OpenIncident("download_failed", modelPath, ex.GetType().Name, ex.Message);
        }
    }

    // One incident per cause: a later take that finds the VAD unusable for the
    // same reason adds nothing, a new cause is news and is reported.
    private void OpenIncident(string reason, string modelPath, string exType, string exMessage)
    {
        lock (_incidentLock)
        {
            if (_incidentReason == reason) return;
            _incidentReason = reason;
        }
        DeckleVadSource.Log.SpeechTrimUnavailable(reason, exType, exMessage);
        DeckleVadSource.Log.SpeechTrimUnavailableDetail(reason, modelPath);
    }

    private void CloseIncident()
    {
        string? previous;
        lock (_incidentLock)
        {
            previous = _incidentReason;
            _incidentReason = null;
        }
        if (previous is not null) DeckleVadSource.Log.SpeechTrimRecovered(previous);
    }

    private static void TryDeleteModel(string modelPath)
    {
        try { File.Delete(modelPath); }
        catch (Exception ex)
        {
            // Best effort: a file that stays blocks the verified download from
            // publishing, and that failure carries the visible cause.
            DeckleVadSource.Log.SpeechTrimVadFileDeleteFailed(modelPath, ex.GetType().Name, ex.Message);
        }
    }

    // Runs at most once per service (the loaded session is cached), so hashing a
    // file of a couple of MB is cheap insurance that the file on disk is exactly
    // the build we expect — not an older version or a corrupt copy. A file that
    // cannot be read or hashed counts as a mismatch so it gets replaced; the
    // caller reports readError as the cause.
    private static bool FileMatchesExpectedSha(string modelPath, out Exception? readError)
    {
        readError = null;
        try
        {
            using FileStream stream = File.OpenRead(modelPath);
            byte[] hash = SHA256.HashData(stream);
            return string.Equals(
                Convert.ToHexString(hash), SileroVadModel.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            readError = ex;
            return false;
        }
    }

    public void Dispose()
    {
        _vad?.Dispose();
        _vad = null;
    }
}
