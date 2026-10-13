using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Autocorrect;

// The default events carry counts, lengths and reasons only — no typed text on
// the always-on path. A few opt-in, consent-gated events deliberately carry words
// (the per-word decision and reranker records, the typed-sentence corpus, the
// typing stream), routed to dedicated datasets and off by default.
[EventSource(Name = "Deckle-Autocorrect")]
public sealed class DeckleAutocorrectSource : DeckleEventSource
{
    public static readonly DeckleAutocorrectSource Log = new();

    private DeckleAutocorrectSource() { }

    internal static bool IsActivityDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Autocorrect, Log, level, keywords);

    // Ids 6 and 20 are retired: never reuse them, old logs carry them.
    public const int EvtEngineStarted      = 1;
    public const int EvtEngineStopped      = 2;
    public const int EvtSurfaceChanged     = 3;
    public const int EvtCorrectionApplied  = 4;
    public const int EvtCorrectionDetail   = 5;
    public const int EvtInjectionFailed    = 7;
    public const int EvtLearningSignal     = 8;
    public const int EvtActivityRollup     = 9;
    public const int EvtEnrollmentSuggested = 10;
    public const int EvtLexiconLoadComplete = 11;
    public const int EvtEngineReady         = 12;
    public const int EvtRerankerStatus      = 13;
    public const int EvtRerankSlotPending   = 14;
    public const int EvtRerankSubmitted     = 15;
    public const int EvtRerankVerdict       = 16;
    public const int EvtAutocorrectDecision = 17;
    public const int EvtAutocorrectRerank   = 18;
    public const int EvtAutocorrectText     = 19;
    public const int EvtAutocorrectStream   = 21;
    // Retired; the published event id stays reserved.
    public const int EvtPausePassTriggered  = 22;
    public const int EvtInjectionIncident   = 23;
    public const int EvtInjectionRecovered  = 24;
    public const int EvtInjectionEpisodeDetail = 25;
    public const int EvtRerankerLoadFailed  = 26;
    public const int EvtSentenceStageAbandoned = 27;
    public const int EvtPersonalDictionarySanitized = 28;
    public const int EvtExternalMutationBurst = 29;
    public const int EvtDomainPackMerged      = 30;
    public const int EvtCaretSentenceRecovery = 31;

    [Event(EvtCaretSentenceRecovery,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "caret sentence recovery | outcome={0} | boundary={1} | text_len={2}")]
    public void CaretSentenceRecovery(string outcome, string boundary, int text_len)
    {
        if (IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtCaretSentenceRecovery, outcome, boundary, text_len);
    }

    // ── Engine lifecycle ─────────────────────────────────────────────────

    [Event(EvtEngineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autocorrect armed")]
    public void EngineStarted()
    {
        if (IsEnabled()) WriteEvent(EvtEngineStarted);
    }

    [Event(EvtEngineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autocorrect stopped")]
    public void EngineStopped()
    {
        if (IsEnabled()) WriteEvent(EvtEngineStopped);
    }

    // load_ms is the wall-clock of the off-UI-thread lexical build, the
    // contextual model load excluded; entries is the effective lexicon's form
    // count. Verbose so the number never reaches the Info milestone stream.
    [Event(EvtLexiconLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "lexicon load complete | load_ms={0} | entries={1}")]
    public void LexiconLoadComplete(long load_ms, int entries)
    {
        if (IsEnabled()) WriteEvent(EvtLexiconLoadComplete, load_ms, entries);
    }

    // One active domain pack fused into the effective lexicon. forms is what
    // the pack artifact carries, not what it added: max-wins absorbs a form the
    // base already holds, so these counts do not add up to LexiconLoadComplete's
    // post-merge entries.
    [Event(EvtDomainPackMerged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "domain pack merged | pack={0} | forms={1}")]
    public void DomainPackMerged(string pack, int forms)
    {
        if (IsEnabled()) WriteEvent(EvtDomainPackMerged, pack, forms);
    }

    // Readiness edge: the engine is built, wired and reconciled to the persisted
    // settings. No number: its verbose sibling LexiconLoadComplete carries the timing.
    [Event(EvtEngineReady,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autocorrect ready")]
    public void EngineReady()
    {
        if (IsEnabled()) WriteEvent(EvtEngineReady);
    }

    // Which contextual (sentence-stage) engine came up: engine names the winner
    // of the composition-root preference order (RerankerEngines vocabulary),
    // load_ms its wall-clock model load.
    [Event(EvtRerankerStatus,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autocorrect contextual stage | engine={0} | load_ms={1}")]
    public void RerankerStatus(string engine, long load_ms)
    {
        if (IsEnabled()) WriteEvent(EvtRerankerStatus, engine, load_ms);
    }

    // A present contextual model that could not initialize is a durable incident,
    // not activity detail. exception is Exception.ToString(): type, message, inner
    // exception and stack stay together so native loader failures are actionable.
    [Event(EvtRerankerLoadFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Contextual correction model failed to load | engine={0} | exception={1}")]
    public void RerankerLoadFailed(string engine, string exception)
    {
        if (IsEnabled()) WriteEvent(EvtRerankerLoadFailed, engine, exception);
    }

    // Closed vocabulary for RerankerStatus.engine.
    public static class RerankerEngines
    {
        public const string SentenceJudge = "sentence_judge"; // ONNX GenAI Qwen judge (DirectML)
        public const string Camembert     = "camembert";      // masked-LM reranker
        public const string None          = "none";           // deterministic rules only
    }

    // Count-only confirmation that the lexical admission policy removed learned
    // entries which can no longer be trusted. The words themselves stay solely
    // in the inspectable personal-dictionary file and never enter this log.
    [Event(EvtPersonalDictionarySanitized,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "personal dictionary sanitized | removed={0}")]
    public void PersonalDictionarySanitized(int removed)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPersonalDictionarySanitized, removed);
    }

    // ── Surface gate ─────────────────────────────────────────────────────

    [Event(EvtSurfaceChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "surface | process={0} | editable={1} | password={2} | enrolled={3} | {4}")]
    public void SurfaceChanged(string process, bool editable, bool password, bool enrolled, string probe)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Capture)) return;
        WriteEvent(EvtSurfaceChanged, process, editable, password, enrolled, probe);
    }

    // ── Corrections ──────────────────────────────────────────────────────

    [Event(EvtCorrectionApplied,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Corrected a word")]
    public void CorrectionApplied()
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtCorrectionApplied);
    }

    [Event(EvtCorrectionDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "correction | reason={0} | original_len={1} | replacement_len={2} | backspaces={3}")]
    public void CorrectionDetail(string reason, int original_len, int replacement_len, int backspaces)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtCorrectionDetail, reason, original_len, replacement_len, backspaces);
    }

    [Event(EvtInjectionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "correction injection failed | backspaces={0} | text_len={1}")]
    public void InjectionFailed(int backspaces, int text_len)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailed, backspaces, text_len);
    }

    [Event(EvtInjectionIncident,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Text injection is failing — corrections may not reach this app")]
    public void InjectionIncident()
    {
        if (IsEnabled()) WriteEvent(EvtInjectionIncident);
    }

    [Event(EvtInjectionRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Text injection recovered")]
    public void InjectionRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtInjectionRecovered);
    }

    [Event(EvtInjectionEpisodeDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "injection episode | outcome={0} | failures={1} | backspaces={2} | text_len={3}")]
    public void InjectionEpisodeDetail(
        string outcome, int failures, int backspaces, int text_len)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtInjectionEpisodeDetail, outcome, failures, backspaces, text_len);
    }

    // ── Contextual reranker decisions ─────────────────────────────────────
    //
    // The slot's life across the deferred second stage, so a "why didn't it
    // correct?" reads off the trace: a SlotPending with no Submitted means the
    // sentence never closed into a request; a Submitted with no Verdict means the
    // inference is still running or died; an abstain/stale Verdict says the model
    // declined or the sentence was reset under it. Counts and a closed outcome
    // vocabulary only — no typed text on these pipeline events.

    // A word with two or more candidates is now an open slot, waiting for the
    // sentence stage to decide it. candidates is the larger of its slot and
    // whole-sentence candidate sets.
    [Event(EvtRerankSlotPending,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rerank slot pending | candidates={0} | word_len={1}")]
    public void RerankSlotPending(int candidates, int word_len)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtRerankSlotPending, candidates, word_len);
    }

    // A terminal sentence ender handed a closed decision to the background
    // reranker. slot is -1 for the production whole-sentence transaction; a
    // non-negative value is a compatibility slot-mode request, which production
    // never sends.
    [Event(EvtRerankSubmitted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rerank submitted | slot={0} (-1=whole_sentence) | context_words={1}")]
    public void RerankSubmitted(int slot, int context_words)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtRerankSubmitted, slot, context_words);
    }

    // The reranker verdict landed. outcome is a closed vocabulary: applied (slot
    // rewritten), equal (model chose the typed form), abstain (model not
    // confident — left as typed), stale (the sentence was reset under the
    // in-flight request), resolved (slot already decided), expired (the exact
    // visible tail outgrew the bounded rewrite budget), blocked (the in-place
    // rewrite was refused by the target surface). reason carries the closed
    // abstention/error reason when one exists; it is empty for ordinary outcomes.
    [Event(EvtRerankVerdict,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rerank verdict | outcome={0} | reason={1}")]
    public void RerankVerdict(string outcome, string reason)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtRerankVerdict, outcome, reason);
    }

    // A reset made the current sentence-stage work unsafe before it completed.
    // reason is the ResetReason closed vocabulary; pending_slots includes work
    // still waiting for context and the request already in flight. This is emitted
    // at abandonment time, not when a late result eventually returns as stale.
    [Event(EvtSentenceStageAbandoned,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "sentence stage abandoned | reason={0} | pending_slots={1} | in_flight={2}")]
    public void SentenceStageAbandoned(string reason, int pending_slots, bool in_flight)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtSentenceStageAbandoned, reason, pending_slots, in_flight);
    }

    [Event(EvtExternalMutationBurst,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "external mutation | outcome={0} | backspaces={1} | text_units={2}")]
    public void ExternalMutationBurst(string outcome, int backspaces, int text_units)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtExternalMutationBurst, outcome, backspaces, text_units);
    }

    public static class ExternalMutationOutcomes
    {
        public const string Reconciled = "reconciled";
        public const string Unmodeled = "unmodeled";
        public const string Unsupported = "unsupported";
        public const string ProtectedSurface = "protected_surface";
    }

    // ── Correction decision dataset ──────────────────────────────────────
    //
    // The per-word decision ledger, the deep "why did(n't) it correct?" surface.
    // These two DELIBERATELY carry text: the typed word, its left context, and the
    // candidate forms with their scores. They are routed solely to the opt-in
    // autocorrect.decisions.jsonl sink (gated by AutocorrectDecisions, off by
    // default) and excluded from app.jsonl. The synchronous decision and its
    // deferred reranker verdict share `id`, so the two lines join. CorrectionTrace
    // renders the flat fields.

    [Event(EvtAutocorrectDecision,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "decision | {1} → {3} | {4}/{5}")]
    public void AutocorrectDecisionRecorded(
        long   id,
        string word,
        string context,
        string outcome,
        string stage,
        string reason,
        string candidates,
        string gauges,
        string trail)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtAutocorrectDecision,
            id, word, context, outcome, stage, reason, candidates, gauges, trail);
    }

    [Event(EvtAutocorrectRerank,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "rerank | {1} → {3} | {2} | {5}")]
    public void AutocorrectRerankRecorded(
        long   id,
        string word,
        string outcome,
        string chosen,
        string scores,
        string margin)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtAutocorrectRerank, id, word, outcome, chosen, scores, margin);
    }

    // ── Typed-sentence corpus ─────────────────────────────────────────────
    //
    // One sentence typed on an enrolled, editable, non-password surface; its fields
    // are SentenceCorpus.SentenceRecord's. The heaviest text capture in the module,
    // a verbatim record of typed input: routed solely to the opt-in
    // autocorrect.text.jsonl sink (gated by AutocorrectText, off by default) and
    // excluded from app.jsonl.

    [Event(EvtAutocorrectText,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "text | {0} | {1} | {4}")]
    public void AutocorrectTextRecorded(
        string process, string typed, string final, string history, string closure, string timing)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtAutocorrectText, process, typed, final, history, closure, timing);
    }

    // ── Typing stream ─────────────────────────────────────────────────────
    //
    // One closed run of the typing stream on an ENROLLED correctable surface; its
    // fields and replay contract are TypingStream's. Verbatim typed input: routed
    // solely to the opt-in autocorrect.stream.jsonl sink under the SAME consent
    // envelope as the typed-sentence corpus (gated by AutocorrectText, off by
    // default) and excluded from app.jsonl. Password surfaces never reach the
    // stream: the engine gates them before decoding.

    [Event(EvtAutocorrectStream,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "stream | {0} | {1} | erased={2} | {3}")]
    public void AutocorrectStreamRecorded(
        string process, string text, int erased, string closure, string timing)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtAutocorrectStream, process, text, erased, closure, timing);
    }

    // ── Learning ─────────────────────────────────────────────────────────

    [Event(EvtLearningSignal,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "learning | signal={0}")]
    public void LearningSignal(string signal)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtLearningSignal, signal);
    }

    // ── Activity rollup (30 s aggregate while words commit) ──────────────

    [Event(EvtActivityRollup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "autocorrect activity | commits={0} | corrections={1} | re_edited={2} | learning_signals={3} | gated_surfaces={4}")]
    public void ActivityRollup(int commits, int corrections, int re_edited, int learning_signals, int gated_surfaces)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtActivityRollup, commits, corrections, re_edited, learning_signals, gated_surfaces);
    }

    // ── Enrollment ───────────────────────────────────────────────────────

    [Event(EvtEnrollmentSuggested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "enrollment suggested | process={0}")]
    public void EnrollmentSuggested(string process)
    {
        if (!IsActivityDetailEnabled(EventLevel.Informational, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtEnrollmentSuggested, process);
    }
}
