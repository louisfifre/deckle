using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Structured Heartbeats: Canonical JSONL ──────────────────────────
    //
    // LatencyRecorded, CorpusAsrRecorded, and CorpusRewriteRecorded are the
    // events JsonlSink (and RoutedJsonlSink for both corpora)
    // filters to write latency.jsonl and bucketed corpus.jsonl files. The
    // Message format is a one-line summary for LogWindow; the full payload is
    // serialized by EtwSelfDescribingEventFormat with snake_case names becoming
    // JSON keys.
    //
    // CorpusAsrRecorded captures ASR output (Whisper, later Voxtral). Routed
    // to corpus/<bucket>/<tier>/corpus.jsonl (bucket=raw in word-for-word mode,
    // bucket=voxtral-<instruction> when named-instruction Voxtral mode is
    // wired). The five length tiers — very-short / short / medium / long /
    // very-long — split the dataset by ASR load for analysis.
    //
    // CorpusRewriteRecorded captures LLM rewrite output. Routed to
    // corpus/rewrite-<name>-<id>/corpus.jsonl (flat: no tier on rewrite, see
    // ADR-0006). rewrite_profile_id joins with the profile; prompt_template_hash
    // invalidates analyses if the template changes without ID rename.
    //
    // When a rewrite runs, both events leave with the same transcription_id:
    // this is the key joining lines to the WAV (audio/<transcription_id>.wav).

    [Event(EvtLatencyRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "audio={0:F1}s hotkey={2}ms whisper={6}ms llm={7}ms outcome={21}")]
    public void LatencyRecorded(
        double audio_sec,
        long   model_load_ms,
        long   hotkey_to_capture_ms,
        long   record_drain_ms,
        long   stop_to_pipeline_ms,
        long   whisper_init_ms,
        long   whisper_ms,
        long   llm_ms,
        long   ollama_load_ms,
        long   llm_prompt_eval_ms,
        long   llm_eval_ms,
        int    llm_prompt_tokens,
        int    llm_eval_tokens,
        long   clipboard_ms,
        long   paste_ms,
        string strategy,
        int    n_segments,
        int    text_chars,
        int    text_words,
        string profile,
        bool   pasted,
        string outcome)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtLatencyRecorded,
            audio_sec, model_load_ms, hotkey_to_capture_ms, record_drain_ms,
            stop_to_pipeline_ms, whisper_init_ms,
            whisper_ms, llm_ms, ollama_load_ms, llm_prompt_eval_ms, llm_eval_ms,
            llm_prompt_tokens, llm_eval_tokens, clipboard_ms, paste_ms,
            strategy, n_segments, text_chars, text_words, profile, pasted, outcome);
    }

    [Event(EvtCorpusAsrRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "asr | bucket={2} | tier={3} | words={9} | wps={12:F1} | audio={14}")]
    public void CorpusAsrRecorded(
        string transcription_id,
        string audio_file,
        string bucket,
        string tier,
        string backend,
        string model,
        string language,
        string prompt_or_instruction,
        string text,
        int    text_words,
        int    text_chars,
        double duration_seconds,
        double words_per_second,
        long   elapsed_ms,
        string audio_content)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtCorpusAsrRecorded,
            transcription_id, audio_file, bucket, tier,
            backend, model, language, prompt_or_instruction,
            text, text_words, text_chars, duration_seconds,
            words_per_second, elapsed_ms, audio_content);
    }

    [Event(EvtCorpusRewriteRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "rewrite | bucket={2} | profile={4} | words={9} | elapsed_ms={11}")]
    public void CorpusRewriteRecorded(
        string transcription_id,
        string audio_file,
        string bucket,
        string rewrite_profile_id,
        string rewrite_profile_name,
        string ollama_endpoint,
        string ollama_model,
        string prompt_template_hash,
        string text,
        int    text_words,
        int    text_chars,
        long   elapsed_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtCorpusRewriteRecorded,
            transcription_id, audio_file, bucket,
            rewrite_profile_id, rewrite_profile_name,
            ollama_endpoint, ollama_model, prompt_template_hash,
            text, text_words, text_chars, elapsed_ms);
    }
}
