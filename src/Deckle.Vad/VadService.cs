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

        if (File.Exists(modelPath))
        {
            // Time the hash verify + ONNX session construction together: both run
            // at most once per service, on the first streaming take's critical path.
            // Pure observability — the timer wraps the existing operations and does
            // not reorder File.Exists / FileMatchesExpectedSha / new SileroVad.
            var sw = Stopwatch.StartNew();
            if (FileMatchesExpectedSha(modelPath))
            {
                try
                {
                    _vad = new SileroVad(modelPath);
                    sw.Stop();
                    DeckleVadSource.Log.SpeechTrimVadLoaded();
                    DeckleVadSource.Log.SpeechTrimVadLoadedDetail(modelPath);
                    DeckleVadSource.Log.SpeechTrimVadLoadComplete(sw.ElapsedMilliseconds);
                    return _vad;
                }
                catch (Exception ex)
                {
                    _loadFailed = true;
                    DeckleVadSource.Log.SpeechTrimVadUnavailable();
                    DeckleVadSource.Log.SpeechTrimVadUnavailableDetail($"load failed: {ex.GetType().Name}: {ex.Message}");
                    // Bytes match the pinned build yet it won't construct — should not
                    // happen, but delete defensively so a later launch retries clean.
                    try { File.Delete(modelPath); } catch { /* best effort */ }
                    return null;
                }
            }

            // Present but not the pinned build — an older model version (so a version
            // bump actually takes effect instead of silently keeping the old file) or
            // a corrupt copy. File.Exists alone can't tell the difference; drop it and
            // fall through to the verified download.
            DeckleVadSource.Log.SpeechTrimVadUnavailable();
            DeckleVadSource.Log.SpeechTrimVadUnavailableDetail("on-disk model checksum mismatch — re-fetching");
            try { File.Delete(modelPath); } catch { /* best effort */ }
        }

        // Model absent — provision it once in the background; this take runs
        // untrimmed, a later take picks the model up.
        if (!_downloadKicked)
        {
            _downloadKicked = true;
            _ = DownloadModelAsync(modelPath);
        }
        return null;
    }

    private static async Task DownloadModelAsync(string modelPath)
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
                DeckleVadSource.Log.SpeechTrimVadUnavailable();
                DeckleVadSource.Log.SpeechTrimVadUnavailableDetail($"download failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            DeckleVadSource.Log.SpeechTrimVadUnavailable();
            DeckleVadSource.Log.SpeechTrimVadUnavailableDetail($"download failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // True when the on-disk file hashes to the pinned SHA-256. Reading + hashing a
    // ~2 MB file is milliseconds and happens at most once per service (the loaded
    // session is cached), so it is cheap insurance that the file on disk is exactly
    // the build we expect — not an older version or a corrupt copy.
    private static bool FileMatchesExpectedSha(string modelPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(modelPath);
            byte[] hash = SHA256.HashData(stream);
            return string.Equals(
                Convert.ToHexString(hash), SileroVadModel.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // Can't read or hash it — treat as a mismatch so it gets replaced.
            DeckleVadSource.Log.SpeechTrimVadUnavailable();
            DeckleVadSource.Log.SpeechTrimVadUnavailableDetail($"checksum read failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _vad?.Dispose();
        _vad = null;
    }
}
