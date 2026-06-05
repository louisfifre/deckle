using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Transcription pre-processing DSP (Deckle.Audio) ─────────────────────
    //
    // The DSP stage lives in Deckle.Audio but is invoked by the orchestrator
    // just before the ASR backend, so — like AutoCalibrated, another
    // audio-calibration concern owned by the orchestrator — its events are
    // emitted on this provider, the orchestrator's own. Keeps the pipeline's
    // observability under one provider and one filter.
    //
    // Two events. TranscriptionPreprocessed is the per-recording (per-utterance
    // in streaming) transform summary — a staged transformation, Pipeline keyword,
    // same family as VAD. PreprocessedTelemetryRecorded is the distribution rollup
    // of the processed signal — the post-DSP mirror of DeckleAudioSource's raw
    // MicrophoneTelemetryRecorded, on Heartbeat so the two distributions sit under
    // the same filter and read before/after term for term. It lives on this provider
    // because the DSP is the orchestrator's concern, not the capture module's.

    [Event(EvtTranscriptionPreprocessed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Pre-processing applied | in={0:F1} dBFS | out={1:F1} dBFS | makeup={2:+0.0;-0.0} dB | peak={3:F3}")]
    public void TranscriptionPreprocessed(double input_dbfs, double output_dbfs, double makeup_db, double output_peak)
    {
        if (IsEnabled()) WriteEvent(EvtTranscriptionPreprocessed, input_dbfs, output_dbfs, makeup_db, output_peak);
    }

    [Event(EvtPreprocessedTelemetryRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "Post-DSP telemetry over {0:F1}s ({1} samples @20Hz): min={2:F1} p10={3:F1} p25={4:F1} p50={5:F1} p75={6:F1} p90={7:F1} max={8:F1} dBFS | mean RMS={9:F4} ({10:F1} dBFS)")]
    public void PreprocessedTelemetryRecorded(
        double duration_seconds,
        int    samples,
        double min_dbfs,
        double p10_dbfs,
        double p25_dbfs,
        double p50_dbfs,
        double p75_dbfs,
        double p90_dbfs,
        double max_dbfs,
        double mean_rms,
        double mean_dbfs,
        double tail_rms,
        double tail_dbfs,
        string tail_state)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtPreprocessedTelemetryRecorded,
            duration_seconds, samples,
            min_dbfs, p10_dbfs, p25_dbfs, p50_dbfs, p75_dbfs, p90_dbfs, max_dbfs,
            mean_rms, mean_dbfs, tail_rms, tail_dbfs, tail_state);
    }

    // ── External Silero VAD pre-trim (Deckle.Inference.Onnx) ────────────────
    //
    // Distinct from the whisper-internal SpeechDetection VAD: this is the
    // orchestrator's own pass that trims each streaming utterance to its speech
    // before the backend and drops a no-speech utterance outright. Same Pipeline
    // family as the DSP above; emitted on this provider because the trim is the
    // orchestrator's concern. The VAD-lifecycle events (load / download) ride here
    // too so the whole external-VAD story reads under one provider.

    [Event(EvtSpeechTrimmed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Speech trim | utt #{0} | {1} → {2} samples | {3} ms")]
    public void SpeechTrimmed(int utterance_index, int in_samples, int out_samples, long trim_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimmed, utterance_index, in_samples, out_samples, trim_ms);
    }

    [Event(EvtUtteranceDroppedNoSpeech,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Utterance #{0} dropped — no speech detected")]
    public void UtteranceDroppedNoSpeech(int utterance_index)
    {
        if (IsEnabled()) WriteEvent(EvtUtteranceDroppedNoSpeech, utterance_index);
    }

    [Event(EvtSpeechTrimVadLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Silero VAD loaded ({0})")]
    public void SpeechTrimVadLoaded(string model_path)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadLoaded, model_path);
    }

    [Event(EvtSpeechTrimVadUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Silero VAD unavailable — {0}")]
    public void SpeechTrimVadUnavailable(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadUnavailable, reason);
    }

    [Event(EvtSpeechTrimVadDownloadStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Downloading Silero VAD model… ({0})")]
    public void SpeechTrimVadDownloadStart(string url)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadDownloadStart, url);
    }

    [Event(EvtSpeechTrimVadDownloadComplete,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Silero VAD model downloaded ({0})")]
    public void SpeechTrimVadDownloadComplete(string model_path)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadDownloadComplete, model_path);
    }

    [Event(EvtSpeechTrimNotReady,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "SpeechTrim enabled but the VAD model isn't ready — this take runs untrimmed")]
    public void SpeechTrimNotReady()
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimNotReady);
    }
}
