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
           Message = "start | audio_sec={0:F1} | samples={1} | strategy={2}")]
    public void TranscribeStartDetail(double audio_sec, int samples, string strategy)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtTranscribeStartDetail, audio_sec, samples, strategy);
    }

    [Event(EvtTranscriptionCorrelation,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "correlation | transcription_id={0}")]
    public void TranscriptionCorrelation(string transcription_id)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtTranscriptionCorrelation, transcription_id);
    }

    [Event(EvtTranscribeParams,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "params | {0}")]
    public void TranscribeParams(string params_text)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtTranscribeParams, params_text);
    }

    [Event(EvtTranscribePromptConfigured,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "prompt configured | len={0} | carry={1}")]
    public void TranscribePromptConfigured(int prompt_len, bool carry)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtTranscribePromptConfigured, prompt_len, carry);
    }

    [Event(EvtTranscribeEmpty,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "There was no audio to transcribe")]
    public void TranscribeEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeEmpty);
    }

    // reason (closed vocabulary): exception (the backend call threw),
    // native_error (the backend returned result_code != 0), incomplete (the
    // consumer could not deliver every utterance). result_code is -1 when no
    // native code applies; ex_type and ex_message are empty unless it threw.
    [Event(EvtTranscriptionFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcription failed | reason={0} | result_code={1} | ex_type={2} | ex_message={3}")]
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
           Message = "complete | whisper_ms={0} | n_seg={1} | chars={2}")]
    public void TranscribeCompleteDetail(long whisper_ms, int n_seg, int chars)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeCompleteDetail, whisper_ms, n_seg, chars);
    }

    [Event(EvtTranscribeRepetitionLoop,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A repetition loop was detected and transcription was aborted")]
    public void TranscribeRepetitionLoop()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeRepetitionLoop);
    }

    [Event(EvtTranscribeRepetitionLoopMetrics,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "repetition loop | streak={0} | period={1}")]
    public void TranscribeRepetitionLoopMetrics(int streak, int period)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtTranscribeRepetitionLoopMetrics, streak, period);
    }

    [Event(EvtTranscribeHallucinationFiltered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A known hallucination was filtered out")]
    public void TranscribeHallucinationFiltered()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeHallucinationFiltered);
    }

    // One per segment left out. `entry` is the catalogue line as written in the
    // code, never the decoded text, so the payload carries nothing the person
    // said. Not behind the Transcription detail control: it explains why text
    // went missing from a take, which a reader needs whatever the verbosity.
    [Event(EvtTranscribeHallucinationFilteredMetrics,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "hallucination filtered | segment={0} | segments={1} | chars={2} | entry={3}")]
    public void TranscribeHallucinationFilteredMetrics(int segment, int segments, int chars, string entry)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeHallucinationFilteredMetrics, segment, segments, chars, entry);
    }

    [Event(EvtTranscribeSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "skip transcribe | state={0}")]
    public void TranscribeSkipped(string state)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeSkipped, state);
    }

}
