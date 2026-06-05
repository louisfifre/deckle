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
            Downloader.DownloadResult result = await Downloader.DownloadAsync(
                SileroVadModel.Url, modelPath, expectedSha256: null,
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
        _vad?.Dispose();
        _vad = null;
    }
}
