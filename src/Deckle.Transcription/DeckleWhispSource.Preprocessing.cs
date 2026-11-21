using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Transcription pre-processing DSP (Deckle.Audio) ─────────────────────
    //
    // The DSP stage lives in Deckle.Audio but the orchestrator invokes it just
    // before the ASR backend, so its events go on the orchestrator's provider:
    // the pipeline's observability stays under one provider and one filter.

    [Event(EvtTranscriptionPreprocessed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "input_dbfs={0,6:F1} | output_dbfs={1,6:F1} | makeup_db={2,5:+0.0;-0.0} | output_peak={3:F3}")]
    public void TranscriptionPreprocessed(double input_dbfs, double output_dbfs, double makeup_db, double output_peak)
    {
        // Once per utterance on the streaming path.
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtTranscriptionPreprocessed, input_dbfs, output_dbfs, makeup_db, output_peak);
    }
}
