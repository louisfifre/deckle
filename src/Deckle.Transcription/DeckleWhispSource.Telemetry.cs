using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Heartbeats structurés — JSONL canoniques ────────────────────────
    //
    // LatencyRecorded, CorpusAsrRecorded et CorpusRewriteRecorded sont
    // les events que JsonlEventListener (et RoutedJsonlEventListener pour
    // les deux corpus) filtrent pour écrire latency.jsonl et les
    // corpus.jsonl bucketés. Le format Message est un récap mono-ligne
    // pour LogWindow ; le payload complet est sérialisé par
    // EtwSelfDescribingEventFormat avec les noms snake_case devenant
    // les clés JSON.
    //
    // CorpusAsrRecorded capture la sortie ASR (Whisper, plus tard
    // Voxtral). Routée vers corpus/<bucket>/<tier>/corpus.jsonl
    // (bucket=raw en mode mot-pour-mot, bucket=voxtral-<instruction>
    // quand le mode instruction-nommée Voxtral sera branché). Les
    // cinq tiers de longueur — very-short / short / medium / long /
    // very-long — découpent le dataset par charge ASR pour l'analyse.
    //
    // CorpusRewriteRecorded capture la sortie réécriture LLM. Routée
    // vers corpus/rewrite-<name>-<id>/corpus.jsonl (plat — pas de tier
    // sur le rewrite, voir ADR-0011). Le rewrite_profile_id sert de
    // jointure avec le profil ; le prompt_template_hash invalide les
    // analyses si le template change sans rename d'ID.
    //
    // Quand un rewrite tourne, les deux events partent avec le même
    // transcription_id — c'est la clé qui joint les lignes au WAV
    // (audio/<transcription_id>.wav).

    [Event(EvtLatencyRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "audio={0:F1}s hotkey={2}ms vad={6}ms whisper={8}ms llm={9}ms outcome={23}")]
    public void LatencyRecorded(
        double audio_sec,
        long   model_load_ms,
        long   hotkey_to_capture_ms,
        long   record_drain_ms,
        long   stop_to_pipeline_ms,
        long   whisper_init_ms,
        long   vad_ms,
        long   vad_inference_ms,
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
            stop_to_pipeline_ms, whisper_init_ms, vad_ms, vad_inference_ms,
            whisper_ms, llm_ms, ollama_load_ms, llm_prompt_eval_ms, llm_eval_ms,
            llm_prompt_tokens, llm_eval_tokens, clipboard_ms, paste_ms,
            strategy, n_segments, text_chars, text_words, profile, pasted, outcome);
    }

    [Event(EvtCorpusAsrRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "asr | bucket={2} | tier={3} | words={9} | wps={12:F1}")]
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
        long   elapsed_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtCorpusAsrRecorded,
            transcription_id, audio_file, bucket, tier,
            backend, model, language, prompt_or_instruction,
            text, text_words, text_chars, duration_seconds,
            words_per_second, elapsed_ms);
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