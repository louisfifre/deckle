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
    // One event: TranscriptionPreprocessed, the per-recording transform summary
    // (Verbose, Pipeline keyword — a staged signal transformation, same family
    // as VAD).

    [Event(EvtTranscriptionPreprocessed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Pre-processing applied | in={0:F1} dBFS | out={1:F1} dBFS | makeup={2:+0.0;-0.0} dB | peak={3:F3}")]
    public void TranscriptionPreprocessed(double input_dbfs, double output_dbfs, double makeup_db, double output_peak)
    {
        if (IsEnabled()) WriteEvent(EvtTranscriptionPreprocessed, input_dbfs, output_dbfs, makeup_db, output_peak);
    }
}
