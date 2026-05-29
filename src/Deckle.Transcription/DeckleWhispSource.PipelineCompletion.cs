using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Pipeline completion ─────────────────────────────────────────────

    [Event(EvtPipelineCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Done ({0})")]
    public void PipelineCompleted(string outcome)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineCompleted, outcome);
    }

    [Event(EvtPipelineTimings,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "timings | audio_sec={0:F1} | model_load_ms={1} | hotkey_to_capture_ms={2} | record_drain_ms={3} | stop_to_pipeline_ms={4} | whisper_init_ms={5} | vad_ms={6} | vad_inference_ms={7} | whisper_ms={8} | llm_ms={9} | clipboard_ms={10} | paste_ms={11}")]
    public void PipelineTimings(double audio_sec, long model_load_ms, long hotkey_to_capture_ms, long record_drain_ms, long stop_to_pipeline_ms, long whisper_init_ms, long vad_ms, long vad_inference_ms, long whisper_ms, long llm_ms, long clipboard_ms, long paste_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtPipelineTimings, audio_sec, model_load_ms, hotkey_to_capture_ms, record_drain_ms, stop_to_pipeline_ms, whisper_init_ms, vad_ms, vad_inference_ms, whisper_ms, llm_ms, clipboard_ms, paste_ms);
    }

    [Event(EvtPipelineLlmMetrics,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "llm_metrics | ollama_load_ms={0} | prompt_eval_ms={1} | eval_ms={2} | prompt_tokens={3} | eval_tokens={4}")]
    public void PipelineLlmMetrics(long ollama_load_ms, long prompt_eval_ms, long eval_ms, int prompt_tokens, int eval_tokens)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineLlmMetrics, ollama_load_ms, prompt_eval_ms, eval_ms, prompt_tokens, eval_tokens);
    }

    [Event(EvtPipelineOutputs,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "outputs | n_seg={0} | chars={1} | words={2} | strategy={3} | profile={4} | outcome={5}")]
    public void PipelineOutputs(int n_seg, int chars, int words, string strategy, string profile, string outcome)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineOutputs, n_seg, chars, words, strategy, profile, outcome);
    }
}