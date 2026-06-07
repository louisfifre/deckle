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
                // A model that fails to load is corrupt or incompatible. Delete it so
                // the next launch re-downloads (now checksum-verified) instead of
                // re-finding the same bad file forever — File.Exists above would
                // otherwise short-circuit the download for good. Best effort: if the
                // delete itself fails the feature simply stays off, never worse.
                try { File.Delete(modelPath); } catch { /* best effort */ }
                return null;
            }
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
