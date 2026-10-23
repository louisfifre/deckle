using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Pipeline completion ─────────────────────────────────────────────

    [Event(EvtPipelineCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "outcome={0}")]
    public void PipelineCompletedDetail(string outcome)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineCompletedDetail, outcome);
    }

    [Event(EvtDictationPasted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Dictation completed and pasted")]
    public void DictationPasted()
    {
        if (IsEnabled()) WriteEvent(EvtDictationPasted);
    }

    [Event(EvtDictationCopied,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Dictation completed and copied to the clipboard")]
    public void DictationCopied()
    {
        if (IsEnabled()) WriteEvent(EvtDictationCopied);
    }

    [Event(EvtFileTranscriptionCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "File transcription completed and saved")]
    public void FileTranscriptionCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionCompleted);
    }

    [Event(EvtPipelineTimings,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "audio_s={0:F1} | model_load_ms={1} | hotkey_to_capture_ms={2} | record_drain_ms={3} | stop_to_pipeline_ms={4} | whisper_init_ms={5} | whisper_ms={6} | llm_ms={7} | clipboard_ms={8} | paste_ms={9}")]
    public void PipelineTimings(double audio_s, long model_load_ms, long hotkey_to_capture_ms, long record_drain_ms, long stop_to_pipeline_ms, long whisper_init_ms, long whisper_ms, long llm_ms, long clipboard_ms, long paste_ms)
    {
        if (!IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtPipelineTimings, audio_s, model_load_ms, hotkey_to_capture_ms, record_drain_ms, stop_to_pipeline_ms, whisper_init_ms, whisper_ms, llm_ms, clipboard_ms, paste_ms);
    }

    // The take keeps its raw transcript although a rewriting profile was
    // active. reason (closed vocabulary): empty_result (the rewriter returned
    // no text; its own provider logs why), copy_failed (the rewritten text
    // could not replace the raw copy on the clipboard, so the raw text is
    // what gets pasted).
    [Event(EvtRewriteFellBackToRaw,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The rewrite was not applied, the raw transcript is delivered\nreason={0} | llm_ms={1}")]
    public void RewriteFellBackToRaw(string reason, long llm_ms)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteFellBackToRaw, reason, llm_ms);
    }

    // ── Corpus audio: one incident until a write succeeds ───────────────

    [Event(EvtCorpusAudioWriteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The corpus audio could not be saved, corpus lines go without audio\nex_type={0} | ex_message={1}")]
    public void CorpusAudioWriteFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCorpusAudioWriteFailed, ex_type, ex_message);
    }

    [Event(EvtCorpusAudioWriteRetryFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "ex_type={0} | ex_message={1}")]
    public void CorpusAudioWriteRetryFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCorpusAudioWriteRetryFailed, ex_type, ex_message);
    }

    [Event(EvtCorpusAudioWriteRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Corpus audio is saved again")]
    public void CorpusAudioWriteRecovered(int failures)
    {
        if (IsEnabled()) WriteEvent(EvtCorpusAudioWriteRecovered, failures);
    }

    [Event(EvtPipelineLlmMetrics,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "ollama_load_ms={0} | prompt_eval_ms={1} | eval_ms={2} | prompt_tokens={3} | eval_tokens={4}")]
    public void PipelineLlmMetrics(long ollama_load_ms, long prompt_eval_ms, long eval_ms, int prompt_tokens, int eval_tokens)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtPipelineLlmMetrics, ollama_load_ms, prompt_eval_ms, eval_ms, prompt_tokens, eval_tokens);
    }

    [Event(EvtPipelineOutputs,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "n_seg={0} | chars={1} | words={2} | strategy={3} | profile={4} | outcome={5}")]
    public void PipelineOutputs(int n_seg, int chars, int words, string strategy, string profile, string outcome)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtPipelineOutputs, n_seg, chars, words, strategy, profile, outcome);
    }
}
