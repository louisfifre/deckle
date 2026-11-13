using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Structured Heartbeats: Canonical JSONL ──────────────────────────
    //
    // CorpusAsrRecorded and CorpusRewriteRecorded are the events
    // RoutedJsonlSink filters to write the bucketed corpus.jsonl files. The
    // Message format is useful to direct EventListener diagnostics; operational
    // sinks reject these dataset-tagged events. The full payload is serialized
    // by EtwSelfDescribingEventFormat with snake_case names becoming JSON keys.
    //
    // CorpusAsrRecorded captures ASR output, routed to
    // corpus/<bucket>/<tier>/corpus.jsonl (bucket=raw). The five length tiers
    // (CorpusTier) split the dataset by ASR load for analysis.
    //
    // CorpusRewriteRecorded captures LLM rewrite output, routed flat (no tier) to
    // corpus/rewrite-<name>-<id>/corpus.jsonl. rewrite_profile_id joins with the
    // profile; prompt_template_hash invalidates analyses if the template changes
    // without ID rename.
    //
    // When a rewrite runs, both events leave with the same transcription_id:
    // this is the key joining lines to the WAV (audio/<transcription_id>.wav).

    [Event(EvtCorpusAsrRecorded,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "bucket={2} | tier={3} | words={9} | wps={12:F1} | audio={14}")]
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
        if (!base.IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtCorpusAsrRecorded,
            transcription_id, audio_file, bucket, tier,
            backend, model, language, prompt_or_instruction,
            text, text_words, text_chars, duration_seconds,
            words_per_second, elapsed_ms, audio_content);
    }

    [Event(EvtCorpusRewriteRecorded,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "bucket={2} | profile={4} | words={9} | elapsed_ms={11}")]
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
        if (!base.IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtCorpusRewriteRecorded,
            transcription_id, audio_file, bucket,
            rewrite_profile_id, rewrite_profile_name,
            ollama_endpoint, ollama_model, prompt_template_hash,
            text, text_words, text_chars, elapsed_ms);
    }

    // RecordCorpus returned without writing one of its events. reason names the
    // gate, never text: corpus_disabled and file_run (nothing is recorded),
    // no_profile (the ASR line was written, the rewrite line was not).
    [Event(EvtCorpusRecordSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "reason={0}")]
    public void CorpusRecordSkipped(string reason)
    {
        if (!base.IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtCorpusRecordSkipped, reason);
    }
}
