using Deckle.Audio;
using Deckle.Audio.Preprocessing;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Transcription.Engine;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── MonolithicPipeline partial — the delivered capture→one-call path ───────
    //
    // Monolithic strategy: capture the whole take, then a single
    // backend.TranscribeAsync() that does its own internal windowing (30 s +
    // dynamic seek in Whisper) and inter-window context propagation via tokens.
    // No chunking on the C# side. This is the path the streaming socle is meant
    // to eventually replace; it is kept behind the strategy selector and is the
    // default until streaming proves out (see PipelineStrategyKind).
    //
    // It owns capture and every state transition through to Transcribing,
    // emitting the same logs / UserFeedback / status the inline pipeline did, in
    // the same order — so behaviour is identical to before the seam. It returns
    // the raw text + audio + backend timings for the shared FinalizeTranscription,
    // or null when it already handled an early exit (mic error, empty audio,
    // backend failure, lost CAS) and raised Finished itself.
    //
    // Runs on the worker thread; producerCt is the run's _recordCts token (Stop
    // and Dispose cancel it to drain capture, and it is the abort signal for the
    // single backend call).
    private async Task<PipelineProduction?> ProduceMonolithicAsync(CancellationToken producerCt)
    {
        // ── Capture ───────────────────────────────────────────────────────────
        CaptureResult capture = _capture.Record(_recordingHost, producerCt);
        _recordDrainDuration = capture.DrainDuration;

        if (capture.Outcome == CaptureOutcome.MicError)
        {
            var (title, body) = LocalizeMicError(MicErrorKind.Unavailable, capture.MmsysErr);
            DeckleWhispSource.Log.RecordingMicError(capture.MmsysErr, title);
            EmitUserFeedback(FB_ERROR, title, body, FB_REPLACEMENT);
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        if (capture.Outcome == CaptureOutcome.CapHit)
        {
            // CAS Recording → Stopping ourselves so the transition sequence below
            // stays uniform with the user-driven Stop path (which RequestToggle
            // already moved to Stopping). The drain ran inside Record(), so the
            // stop-to-pipeline stopwatch only covers the post-Record overhead on
            // this rare path (acceptable drift, unchanged from before the seam).
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)PipelineState.Stopping,
                    (int)PipelineState.Recording)
                == (int)PipelineState.Recording)
            {
                _stopToPipelineSw = System.Diagnostics.Stopwatch.StartNew();
            }
        }

        // Auto-calibration enveloppe — pure compute in MicrophoneCalibrationCalculator;
        // the ring buffer + side effects stay on the engine (TryAutoCalibrate).
        if (capture.Telemetry is not null)
        {
            TryAutoCalibrate(capture.Telemetry);
        }

        float[] audio = capture.Pcm;

        // Record() returned because RequestToggle CAS'd Recording → Stopping
        // (user Stop) or the cap-duration branch CAS'd it above. Either way the
        // state should be Stopping; move to Transcribing. Losing this CAS means
        // Dispose won — skip transcription.
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
        if (!_backend.IsModelLoaded)
        {
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

        // Stop the pre-backend overhead stopwatch the moment we hand off to the
        // backend. The backend's TranscriptionResult.InitDurationMs covers the
        // pre-VAD phase inside the inference call itself.
        if (_stopToPipelineSw is { IsRunning: true }) _stopToPipelineSw.Stop();

        // Transcription pre-processing DSP — terminal float[]→float[] stage,
        // applied to a separate buffer fed to the backend only: the raw `audio`
        // stays untouched and is what the corpus stores (ADR-0006). Self-adjusting
        // when the user opts in; a near no-op on a mic already at target.
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

        // Single backend call. Segments stream through the sink to NewSegment so
        // external subscribers (HUD, LogWindow) keep their contract.
        TranscriptionResult result;
        try
        {
            result = await _backend.TranscribeAsync(
                backendAudio,
                seg => NewSegment?.Invoke(seg),
                producerCt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // An OCE here means the pipeline token (Stop, hotkey, shutdown) fired
            // during whisper_full(). Trace the cancellation before the legacy
            // behaviour reclassifies it as TranscribeFailed.
            if (ex is OperationCanceledException)
            {
                DeckleCancellationSource.Log.OperationCancelled(
                    "whisp-transcribe", "upstream", -1);
            }
            DeckleWhispSource.Log.TranscribeFailed(-1);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_TranscriptionFailed_Title"),
                Loc.Get("Engine_TranscriptionFailed_Body"),
                FB_REPLACEMENT);
            RaiseStatus(Loc.Get("Status_TranscriptionFailed"));
            RaiseFinished(TranscriptionOutcome.None);
            DeckleWhispSource.Log.SegmentCallbackThrew(ex.GetType().Name, ex.Message);
            return null;
        }

        // A non-zero result code paired with an abort means the backend bailed on
        // our signal — segments produced before the abort are still usable, so
        // fall through. A non-zero result without an abort is a real failure.
        if (result.ResultCode != 0 && !result.Aborted)
        {
            DeckleWhispSource.Log.TranscribeFailed(result.ResultCode);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_TranscriptionFailed_Title"),
                Loc.Get("Engine_TranscriptionFailed_Body"),
                FB_REPLACEMENT);
            RaiseStatus(Loc.Get("Status_TranscriptionFailed"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        string fullText = result.FullText;
        int nSeg = result.Segments.Count;

        DeckleWhispSource.Log.TranscribeCompleted(nSeg);
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
