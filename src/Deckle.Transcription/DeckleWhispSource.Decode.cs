using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Transcribe ──────────────────────────────────────────────────────

    [Event(EvtTranscribeStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcribing")]
    public void TranscribeStarted()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeStarted);
    }

    [Event(EvtTranscribeStartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "audio_sec={0:F1} | samples={1} | strategy={2}")]
    public void TranscribeStartDetail(double audio_sec, int samples, string strategy)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtTranscribeStartDetail, audio_sec, samples, strategy);
    }

    [Event(EvtTranscribeParams,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "params_text={0}")]
    public void TranscribeParams(string params_text)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtTranscribeParams, params_text);
    }

    // Per call, unlike TranscribeParams (first call only): what this call
    // really ran with. A backend with no prompt or temperature ladder (Parakeet)
    // logs zeros for those columns. last_end_s against audio_s shows whether
    // decoding stopped short of the end of the audio.
    [Event(EvtTranscribeDecodeUsed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "offset_s={0,7:F2} | prompt_chars={1,4} | continues_previous={2,-5} | no_context={3,-5} | temp_start={4:F2} | temp_inc={5:F2} | segments={6,3} | last_end_s={7,7:F2} | audio_s={8,7:F2} | aborted={9}")]
    public void TranscribeDecodeUsed(
        double offset_s, int prompt_chars, bool continues_previous, bool no_context,
        double temp_start, double temp_inc, int segments, double last_end_s,
        double audio_s, bool aborted)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtTranscribeDecodeUsed, offset_s, prompt_chars, continues_previous, no_context,
            temp_start, temp_inc, segments, last_end_s, audio_s, aborted);
    }

    [Event(EvtTranscribeEmpty,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "There was no audio to transcribe, so the take produced no text")]
    public void TranscribeEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeEmpty);
    }

    // reason (closed vocabulary): exception (the backend call threw),
    // native_error (the backend returned result_code != 0), no_context (the
    // backend had no model loaded when the call came, result_code -1),
    // incomplete (the consumer could not deliver every utterance). The two
    // backend reasons come from TranscriptionResult.ErrorReason. result_code
    // is -1 when no native code applies; ex_type and ex_message are empty
    // unless it threw.
    [Event(EvtTranscriptionFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcription failed, so some or all of the take has no text\nreason={0} | result_code={1} | ex_type={2} | ex_message={3}")]
    public void TranscriptionFailed(string reason, int result_code, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtTranscriptionFailed, reason, result_code, ex_type, ex_message);
    }

    [Event(EvtTranscribeCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcription complete")]
    public void TranscribeCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeCompleted);
    }

    [Event(EvtTranscribeCompleteDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "whisper_ms={0} | n_seg={1} | chars={2}")]
    public void TranscribeCompleteDetail(long whisper_ms, long n_seg, long chars)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeCompleteDetail, whisper_ms, n_seg, chars);
    }

    [Event(EvtTranscribeSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "state={0}")]
    public void TranscribeSkipped(string state)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeSkipped, state);
    }

}
