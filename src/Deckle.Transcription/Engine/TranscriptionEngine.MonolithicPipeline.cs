using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Transcription;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── MonolithicPipeline partial — capture, then one backend call ──────────
    //
    // Capture the whole take, then a single backend.TranscribeAsync() that does
    // its own internal windowing (30 s + dynamic seek in Whisper) and
    // inter-window context propagation. No chunking on the C# side.
    //
    // Owns capture and every state transition through to Transcribing; returns
    // null when it already handled an early exit and raised Finished itself.
    // Runs on the worker thread. producerCt is the run's _recordCts token: Stop
    // and Dispose cancel it to drain capture, and it aborts the backend call.
    private async Task<PipelineProduction?> ProduceMonolithicAsync(
        CancellationToken producerCt, Task<bool> primeTask)
    {
        // ── Capture ───────────────────────────────────────────────────────────
        CaptureResult capture = _capture.Record(_recordingHost, producerCt);
        _recordDrainDuration = capture.DrainDuration;

        if (capture.Outcome == CaptureOutcome.MicError)
        {
            var (title, body) = LocalizeMicError(MicErrorKind.Unavailable, capture.MmsysErr);
            OpenMicrophoneIncident("capture", capture.MmsysErr);
            EmitUserFeedback(FB_ERROR, title, body, FB_REPLACEMENT);
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        if (capture.Outcome == CaptureOutcome.CapHit)
        {
            // CAS Recording → Stopping here so the sequence below stays uniform
            // with the user Stop path (which RequestToggle already moved to
            // Stopping). The drain ran inside Record(), so on this path the
            // stop-to-pipeline stopwatch covers only the post-Record overhead.
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)PipelineState.Stopping,
                    (int)PipelineState.Recording)
                == (int)PipelineState.Recording)
            {
                _stopToPipelineSw = System.Diagnostics.Stopwatch.StartNew();
            }
        }

        if (capture.Telemetry is not null)
        {
            TryAutoCalibrate(capture.Telemetry);
        }

        float[] audio = capture.Pcm;

        // RequestToggle (user Stop) or the cap-hit branch above moved the state
        // to Stopping. Losing this CAS means Dispose won: skip transcription.
        if (Interlocked.CompareExchange(
                ref _state,
                (int)PipelineState.Transcribing,
                (int)PipelineState.Stopping)
            != (int)PipelineState.Stopping)
        {
            DeckleWhispSource.Log.TranscribeSkipped(((PipelineState)Volatile.Read(ref _state)).ToString());
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        RaiseStatus(Loc.Get("Status_Transcribing"));

        // ── Inference ─────────────────────────────────────────────────────────
        // Prime gate. On a short cold take it blocks until the prime finishes;
        // that wait counts toward _stopToPipelineSw because the user genuinely
        // waited for it, hence "at_stop". A failed prime falls through to the
        // IsModelLoaded check (UserFeedback already shown by LoadModel).
        await AwaitPrime(primeTask, "at_stop").ConfigureAwait(false);

        if (!_backend.IsModelLoaded)
        {
            // Without this the take vanishes silently; the load cause sits on ModelUnavailable.
            DeckleWhispSource.Log.TakeDropped("model_not_loaded");
            RaiseStatus(Loc.Get("Status_ModelNotReady"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        if (audio.Length == 0)
        {
            DeckleWhispSource.Log.TranscribeEmpty();
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        if (_stopToPipelineSw is { IsRunning: true }) _stopToPipelineSw.Stop();

        // The opt-in DSP writes a separate buffer fed to the backend only: the
        // raw `audio` stays untouched and is what the normalized corpus stores.
        float[] backendAudio = audio;
        var pp = _host.Audio.Preprocessing;
        if (pp.Enabled)
        {
            var processed = TranscriptionPreprocessor.Process(audio, pp);
            backendAudio = processed.Pcm;
            DeckleWhispSource.Log.TranscriptionPreprocessed(
                processed.InputRmsDbfs, processed.OutputRmsDbfs, processed.MakeupGainDb, processed.OutputPeak);
            EmitPreprocessedTelemetry(backendAudio);
        }

        TranscriptionResult? consumed =
            await ConsumeMonolithicAudioAsync(backendAudio, producerCt).ConfigureAwait(false);
        if (consumed is null)
            return null;

        TranscriptionResult result = consumed;

        string fullText = result.FullText;
        int nSeg = result.Segments.Count;

        DeckleWhispSource.Log.TranscribeCompleted();
        DeckleWhispSource.Log.TranscribeCompleteDetail(result.TotalDurationMs, nSeg, fullText.Length);

        if (string.IsNullOrWhiteSpace(fullText))
        {
            RaiseStatus(Loc.Get("Status_Ready"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        return new PipelineProduction(
            RawText:           fullText,
            RawAudio:          audio,
            BackendAudio:      backendAudio,
            TotalTranscribeMs: result.TotalDurationMs,
            InitMs:            result.InitDurationMs,
            NSegments:         nSeg);
    }
}
