using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Segment callback ────────────────────────────────────────────────

    [Event(EvtSegmentRecognized,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "segment recognized | index={0} | start_s={1:F1} | end_s={2:F1} | duration_s={3:F1} | no_speech={4:F3} | avg_p={5:F3} | min_p={6:F3} | text_tokens={7} | tokens={8} | chars={9}")]
    public void SegmentRecognized(
        int index,
        double start_s,
        double end_s,
        double duration_s,
        double no_speech_probability,
        double average_probability,
        double minimum_probability,
        int text_tokens,
        int tokens,
        int characters)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(
            EvtSegmentRecognized,
            index,
            start_s,
            end_s,
            duration_s,
            no_speech_probability,
            average_probability,
             minimum_probability,
             text_tokens,
             tokens,
             characters);
    }

    [Event(EvtSegmentCallbackThrew,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The segment callback threw")]
    public void SegmentCallbackThrew()
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackThrew);
    }

    [Event(EvtSegmentCallbackThrewDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "segment callback threw | ex_type={0} | ex_message={1}")]
    public void SegmentCallbackThrewDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackThrewDetail, ex_type, ex_message);
    }
}
