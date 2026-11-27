using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Vad;

// Owns the Silero VAD's lifecycle: resolve the model path, verify it, load the
// session (or kick a one-time background download), and dispose. A caller hands in
// how to resolve the models directory and then asks EnsureReady() per take; the
// loaded session is cached for the service's life.
//
// Resolution is lazy and never blocks: if the model is present it loads
// synchronously (a couple of MB, milliseconds); if absent, a background download
// is started once and EnsureReady returns null — the current take runs without
// the trim and a later take picks the model up. The download is tried once per
// session, in that background task, never on the caller's thread; a failure is
// reported, and the next start is the retry.
public sealed class VadService : IDisposable
{
    private readonly Func<string> _resolveModelsDirectory;
    private readonly Func<string, Task<ProvisioningResult>> _provisionModel;

    private SileroVad? _vad;
    private bool _downloadKicked;   // a background download is in flight or done
    private Task? _downloadTask;    // that download, kept so a test can await its end
    private bool _loadFailed;       // constructing the session threw — don't retry

    // The open "takes run untrimmed" incident, null when none. Guarded because the
    // background download reports its failure from a pool thread, and trim
    // outcomes arrive from the streaming consumer task.
    private readonly object _incidentLock = new();
    private string? _incidentReason;

    public VadService(Func<string> resolveModelsDirectory)
        : this(resolveModelsDirectory, ProvisionFromTheNetworkAsync)
    {
    }

    // The download is the thing a test cannot do for real: it reaches the network.
    internal VadService(
        Func<string> resolveModelsDirectory,
        Func<string, Task<ProvisioningResult>> provisionModel)
    {
        _resolveModelsDirectory = resolveModelsDirectory;
        _provisionModel = provisionModel;
    }

    // Ends when the download succeeded or failed.
    internal Task DownloadEnded => _downloadTask ?? Task.CompletedTask;

    // expectedSha256 guards against a corrupt/truncated transfer: the downloader
    // verifies the bytes and discards the .partial on mismatch, so a bad file is
    // never published. CancellationToken.None is deliberate — this fire-and-forget
    // transfer (a couple of MB) outlives Dispose rather than being torn down
    // mid-flight; the OS reclaims it at exit.
    private static Task<ProvisioningResult> ProvisionFromTheNetworkAsync(string modelDirectory)
        => SileroVadModel.ProvisionAsync(modelDirectory, progress: null, CancellationToken.None);

    // Returns the loaded VAD, or null when the model isn't usable yet. Call from a
    // single thread (the engine's worker, whichever pipeline it runs) — there
    // is no concurrency on _vad.
    public SileroVad? EnsureReady()
    {
        if (_vad is not null) return _vad;
        if (_loadFailed) return null;

        string modelPath = ResolveModelPath();
        bool modelPresent = File.Exists(modelPath);

        if (modelPresent)
        {
            // Hash verify and session construction run at most once per service,
            // on the first take that needs it; timed together.
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
                    OpenIncident("load_failed", modelPath, ex.GetType().Name, ex.Message, inTray: true);
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
                OpenIncident("checksum_mismatch", modelPath, "", "", inTray: false);
            else
                OpenIncident("checksum_read_failed", modelPath, readError.GetType().Name, readError.Message, inTray: false);
            TryDeleteModel(modelPath);
        }

        if (!_downloadKicked)
        {
            // A present model already opened the incident with its own cause.
            if (!modelPresent)
                OpenIncident("model_missing", modelPath, "", "", inTray: false);
            _downloadKicked = true;
            _downloadTask = DownloadModelAsync(modelPath);
        }
        return null;
    }

    // The caller runs the loaded VAD itself, so it reports each trim's outcome
    // here: the trim failures of successive takes are one incident, opened by
    // the first and closed by the next trim that succeeds. Each failure stays
    // a Verbose attempt; only the opening reaches the log.
    public void ReportTrimFailure(int utteranceIndex, Exception ex)
    {
        DeckleVadSource.Log.SpeechTrimFailed(utteranceIndex, ex.GetType().Name, ex.Message);
        OpenIncident("trim_failed", ResolveModelPath(), ex.GetType().Name, ex.Message, inTray: true);
    }

    // Closes whatever incident is open: a trim that ran proves the VAD usable,
    // whatever made it unusable before.
    public void ReportTrimSucceeded() => CloseIncident();

    private string ResolveModelPath()
        => Path.Combine(_resolveModelsDirectory(), SileroVadModel.FileName);

    // Runs on the pool, off every caller: one attempt. A failure opens the
    // download_failed incident, which stays until a trim succeeds; the next start
    // tries again.
    private async Task DownloadModelAsync(string modelPath)
    {
        DeckleVadSource.Log.SpeechTrimVadDownloadStart();
        DeckleVadSource.Log.SpeechTrimVadDownloadStartDetail(SileroVadModel.Url);

        (string ExType, string Message)? failure = await TryDownloadOnceAsync(modelPath)
            .ConfigureAwait(false);
        if (failure is null)
        {
            DeckleVadSource.Log.SpeechTrimVadDownloadComplete();
            DeckleVadSource.Log.SpeechTrimVadDownloadCompleteDetail(modelPath);
            // The model is on disk and verified, so the trim comes back with
            // the next take; no need to wait for that take to say so.
            CloseIncident();
            return;
        }

        OpenIncident("download_failed", modelPath, failure.Value.ExType, failure.Value.Message, inTray: true);
    }

    private async Task<(string ExType, string Message)?> TryDownloadOnceAsync(string modelPath)
    {
        try
        {
            ProvisioningResult result = await _provisionModel(Path.GetDirectoryName(modelPath)!)
                .ConfigureAwait(false);
            // The downloader reports a failure as text, not as an exception.
            return result.Success ? null : ("", result.ErrorMessage ?? "");
        }
        catch (Exception ex)
        {
            return (ex.GetType().Name, ex.Message);
        }
    }

    // One incident per cause: a later take that finds the VAD unusable for the
    // same reason adds nothing, a new cause is news and is reported.
    //
    // inTray says whether the person should see it in the tray too. A missing
    // or stale model is replaced by the download this service starts on its
    // own, in seconds: showing it would light the dot for a fix already under
    // way. A download that fails, or a model that will not run, stays until a
    // later success closes it, so those are shown.
    private void OpenIncident(
        string reason, string modelPath, string exType, string exMessage, bool inTray)
    {
        lock (_incidentLock)
        {
            if (_incidentReason == reason) return;
            _incidentReason = reason;
        }
        DeckleVadSource.Log.SpeechTrimUnavailable(reason, exType, exMessage);
        DeckleVadSource.Log.SpeechTrimUnavailableDetail(reason, modelPath);
        if (inTray) UserIncidentRegistry.Instance.Open(UserIncidents.VadModel, reason);
    }

    private void CloseIncident()
    {
        string? previous;
        lock (_incidentLock)
        {
            previous = _incidentReason;
            _incidentReason = null;
        }
        if (previous is null) return;
        DeckleVadSource.Log.SpeechTrimRecovered(previous);
        UserIncidentRegistry.Instance.Close(UserIncidents.VadModel);
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
