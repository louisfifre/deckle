using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Inference.Onnx;
using Deckle.Transcription.Setup;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── External Silero VAD (Deckle.Inference.Onnx) ────────────────────────────
    //
    // Opt-in pre-trim for the streaming path (SpeechTrimSettings): each utterance
    // is trimmed to its speech spans before the backend, and an utterance with no
    // speech is dropped (see ConsumeUtterancesAsync). The VAD session loads once
    // and is reused for the engine's life. Resolution is lazy and never blocks
    // recording: if the model file is present it loads synchronously (a couple of
    // MB, milliseconds); if it is absent, a one-time background download is kicked
    // off and the current take runs without the trim — a later take picks it up.

    private SileroVad? _vad;
    private bool _vadDownloadKicked;   // a background download is in flight or done
    private bool _vadLoadFailed;       // constructing the session threw — don't retry

    // Returns the loaded VAD, or null when the model isn't usable yet. Called from
    // the worker thread at the start of each streaming take, so there is no
    // concurrency on _vad.
    private SileroVad? EnsureVadReady()
    {
        if (_vad is not null) return _vad;
        if (_vadLoadFailed) return null;

        string modelPath = Path.Combine(_host.ResolveModelsDirectory(), SileroVadModel.FileName);

        if (File.Exists(modelPath))
        {
            if (FileMatchesExpectedSha(modelPath))
            {
                try
                {
                    _vad = new SileroVad(modelPath);
                    DeckleWhispSource.Log.SpeechTrimVadLoaded(modelPath);
                    return _vad;
                }
                catch (Exception ex)
                {
                    _vadLoadFailed = true;
                    DeckleWhispSource.Log.SpeechTrimVadUnavailable($"load failed: {ex.GetType().Name}: {ex.Message}");
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
            DeckleWhispSource.Log.SpeechTrimVadUnavailable("on-disk model checksum mismatch — re-fetching");
            try { File.Delete(modelPath); } catch { /* best effort */ }
        }

        // Model absent — provision it once in the background; this take runs
        // untrimmed, a later take picks the model up.
        if (!_vadDownloadKicked)
        {
            _vadDownloadKicked = true;
            _ = DownloadVadModelAsync(modelPath);
        }
        return null;
    }

    private static async Task DownloadVadModelAsync(string modelPath)
    {
        DeckleWhispSource.Log.SpeechTrimVadDownloadStart(SileroVadModel.Url);
        try
        {
            // expectedSha256 guards against a corrupt/truncated transfer: the
            // downloader verifies the bytes and discards the .partial on mismatch,
            // so a bad file is never published. CancellationToken.None is deliberate
            // — this fire-and-forget transfer (a couple of MB) outlives Dispose
            // rather than being torn down mid-flight; the OS reclaims it at exit.
            Downloader.DownloadResult result = await Downloader.DownloadAsync(
                SileroVadModel.Url, modelPath, expectedSha256: SileroVadModel.Sha256,
                progress: null, CancellationToken.None).ConfigureAwait(false);

            if (result.Success)
                DeckleWhispSource.Log.SpeechTrimVadDownloadComplete(modelPath);
            else
                DeckleWhispSource.Log.SpeechTrimVadUnavailable($"download failed: {result.ErrorMessage}");
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.SpeechTrimVadUnavailable($"download failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // True when the on-disk file hashes to the pinned SHA-256. Reading + hashing a
    // ~2.3 MB file is milliseconds and happens at most once per engine (the loaded
    // session is cached), so it is cheap insurance that the file on disk is exactly
    // the build we expect — not an older version or a corrupt copy.
    private static bool FileMatchesExpectedSha(string modelPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(modelPath);
            byte[] hash = System.Security.Cryptography.SHA256.HashData(stream);
            return string.Equals(
                Convert.ToHexString(hash), SileroVadModel.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // Can't read or hash it — treat as a mismatch so it gets replaced.
            DeckleWhispSource.Log.SpeechTrimVadUnavailable($"checksum read failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private void DisposeVad()
    {
        // Safe without a lock against an in-flight Trim, unlike the backend: Dispose()
        // cancels the drain token and joins the worker (which blocks on the consumer)
        // before disposing the backend, and only then calls this. The consumer is the
        // sole Trim caller, so by the time we get here it has already exited — no
        // thread is inside vad.Trim when the session is disposed.
        _vad?.Dispose();
        _vad = null;
    }
}
