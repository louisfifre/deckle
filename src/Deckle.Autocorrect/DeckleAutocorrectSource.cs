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
    // 7 retired.
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
    // 22 retired.
    // 23 retired.
    public const int EvtInjectionRecovered  = 24;
    public const int EvtInjectionEpisodeDetail = 25;
    // 26 retired.
    public const int EvtSentenceStageAbandoned = 27;
    public const int EvtPersonalDictionarySanitized = 28;
    public const int EvtExternalMutationBurst = 29;
    public const int EvtDomainPackMerged      = 30;
    public const int EvtCaretSentenceRecovery = 31;
    public const int EvtRerankStalled         = 32;
    public const int EvtRerankStallRecovered  = 33;
    public const int EvtRerankFailing         = 34;
    public const int EvtRerankRecovered       = 35;
    public const int EvtRerankFailureDetail   = 36;
    public const int EvtRerankLaneStarted     = 37;
    public const int EvtRerankLaneStopped     = 38;
    public const int EvtRerankWorkerFaulted   = 39;
    public const int EvtCaretRecoveryFailing  = 40;
    public const int EvtCaretRecoveryRecovered = 41;
    public const int EvtEngineStartFailed     = 42;
    public const int EvtInjectionFailing      = 43;
    public const int EvtRerankerLoadFailure   = 44;
    public const int EvtSentenceJudgeLoaded   = 45;
    public const int EvtRerankerLoadRetried   = 46;
    public const int EvtSystemLanguagesFallback = 47;
    public const int EvtLearningStoreUnreadable = 48;
    public const int EvtLearningStoreLoaded   = 49;
    public const int EvtPersonalDictionaryReset = 50;
    public const int EvtSentenceJudgeFeatureUnavailable = 51;
    public const int EvtDomainPackManifestUnreadable = 52;

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
           Tags = ObservationTags.Journal,
           Message = "Autocorrect armed")]
    public void EngineStarted()
    {
        if (IsEnabled()) WriteEvent(EvtEngineStarted);
    }

    [Event(EvtEngineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Autocorrect stopped")]
    public void EngineStopped()
    {
        if (IsEnabled()) WriteEvent(EvtEngineStopped);
    }

    // The engine was asked to arm and could not: reason is the closed
    // EngineStartFailureReasons vocabulary. Without it a failed arm looks like an
    // engine that was never asked to start.
    [Event(EvtEngineStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Autocorrect failed to arm | reason={0}")]
    public void EngineStartFailed(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtEngineStartFailed, reason);
    }

    public static class EngineStartFailureReasons
    {
        public const string HostStartFailed = "host_start_failed";
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
           Tags = ObservationTags.Journal,
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
           Tags = ObservationTags.Journal,
           Message = "Autocorrect contextual stage | engine={0} | load_ms={1}")]
    public void RerankerStatus(string engine, long load_ms)
    {
        if (IsEnabled()) WriteEvent(EvtRerankerStatus, engine, load_ms);
    }

    // A present contextual model that could not initialize is a durable incident,
    // not activity detail. provider is the execution provider the load asked for
    // ("dml", "cpu"), because a DirectML failure and a broken export call for
    // different repairs.
    [Event(EvtRerankerLoadFailure,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Contextual correction model failed to load | engine={0} | provider={1} | {2}: {3}")]
    public void RerankerLoadFailure(string engine, string provider, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRerankerLoadFailure, engine, provider, ex_type, ex_message);
    }

    // The sentence judge's identity once its native model is up. model is the
    // model folder's leaf name only: the full path carries the Windows user name.
    // retried says the bounded DirectML enumeration retry absorbed a first failure.
    [Event(EvtSentenceJudgeLoaded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "sentence judge loaded | model={0} | provider={1} | margin={2} | vocab_size={3} | retried={4}")]
    public void SentenceJudgeLoaded(string model, string provider, double margin, int vocab_size, bool retried)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSentenceJudgeLoaded, model, provider, margin, vocab_size, retried);
    }

    // The judge runs, but without a prompt feature its margin was calibrated
    // with (SentenceJudgeFeatures), so its verdicts may drift. Once per load.
    [Event(EvtSentenceJudgeFeatureUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Sentence judge runs without a calibrated prompt feature | feature={0} | {1}: {2}")]
    public void SentenceJudgeFeatureUnavailable(string feature, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSentenceJudgeFeatureUnavailable, feature, ex_type, ex_message);
    }

    public static class SentenceJudgeFeatures
    {
        public const string ChatTemplate = "chat_template";
        public const string BosToken     = "bos_token";
    }

    // The first native model construction failed and the bounded retry is about
    // to run. A run that then succeeds still leaves this trace of the flake.
    [Event(EvtRerankerLoadRetried,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reranker load retried | provider={0} | ex_type={1} | ex_message={2}")]
    public void RerankerLoadRetried(string provider, string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRerankerLoadRetried, provider, ex_type, ex_message);
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

    // An older personal-dictionary schema is discarded wholesale: the user loses
    // learned words and explicit suppressions, so it is a Warning, with counts
    // only. The words stay out of the log.
    [Event(EvtPersonalDictionaryReset,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Personal dictionary reset to a new schema | from_version={0} | to_version={1} | words={2} | suppressions={3}")]
    public void PersonalDictionaryReset(int from_version, int to_version, int words, int suppressions)
    {
        if (IsEnabled()) WriteEvent(EvtPersonalDictionaryReset, from_version, to_version, words, suppressions);
    }

    // The Windows language list could not be read, so the lexicon defaults come
    // from the UI culture instead. fallback is the primary-subtag list actually
    // used ("fr,en"), which explains a domain pack that is off by default.
    [Event(EvtSystemLanguagesFallback,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "System language list unavailable, using the UI culture | fallback={0} | {1}: {2}")]
    public void SystemLanguagesFallback(string fallback, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSystemLanguagesFallback, fallback, ex_type, ex_message);
    }

    // A user-data learning store (mistouch families, surface profiles) exists but
    // cannot be used, so the feature it feeds stays inert. store names the store
    // (mistouch_families, surface_profiles), reason is the LearningStoreReasons
    // vocabulary. Contents never enter the log: a parse fault carries its
    // position, not the parser's message, which quotes the offending token.
    [Event(EvtLearningStoreUnreadable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Learning store unreadable, its feature stays inert | store={0} | reason={1} | {2}: {3}")]
    public void LearningStoreUnreadable(string store, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtLearningStoreUnreadable, store, reason, ex_type, ex_message);
    }

    [Event(EvtLearningStoreLoaded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "learning store loaded | store={0} | records_read={1} | records_skipped={2}")]
    public void LearningStoreLoaded(string store, int records_read, int records_skipped)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtLearningStoreLoaded, store, records_read, records_skipped);
    }

    // A shipped pack's manifest does not parse, so the settings page shows the
    // pack without its figures. Verbose: the pack still corrects, and the page
    // reads the manifest only when it opens.
    [Event(EvtDomainPackManifestUnreadable,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "domain pack manifest unreadable | pack={0} | {1}: {2}")]
    public void DomainPackManifestUnreadable(string pack, string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDomainPackManifestUnreadable, pack, ex_type, ex_message);
    }

    public static class LearningStoreReasons
    {
        public const string MalformedJson = "malformed_json";
        public const string NotAnArray    = "not_an_array";
        public const string ReadFailed    = "read_failed";
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

    // Opens the injection incident. process is the target app; win32_error is
    // SendInput's last error, and 0 is itself a clue: UIPI blocking against an
    // elevated window rejects the burst without setting any error.
    [Event(EvtInjectionFailing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Text injection is failing, corrections may not reach this app | process={0} | win32_error={1}")]
    public void InjectionFailing(string process, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailing, process, win32_error);
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

    // ── Contextual stage health ──────────────────────────────────────────
    //
    // Two incidents, each opened once and closed once. A stall: one inference
    // has not returned, and single-flight means every later sentence waits
    // behind it. A failure run: inference returns, but by throwing (DirectML
    // device loss, out of memory), so every verdict is an abstention.

    // age_ms is how long the in-flight request has been running when a later
    // sentence found the stage still busy; reason is RerankStallReasons.
    [Event(EvtRerankStalled,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Contextual correction stalled, later sentences wait | age_ms={0} | reason={1}")]
    public void RerankStalled(long age_ms, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtRerankStalled, age_ms, reason);
    }

    public static class RerankStallReasons
    {
        public const string InferenceStalled = "inference_stalled";
    }

    [Event(EvtRerankStallRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Contextual correction resumed after a stall | elapsed_ms={0}")]
    public void RerankStallRecovered(long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtRerankStallRecovered, elapsed_ms);
    }

    // source names where the exception surfaced (RerankFailureSources); hr is
    // Exception.HResult, the field that tells a DXGI device removal apart.
    [Event(EvtRerankFailing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Contextual correction is failing | source={0} | {1}: {2} | hr={3} | consecutive={4}")]
    public void RerankFailing(string source, string ex_type, string ex_message, int hr, int consecutive)
    {
        if (IsEnabled()) WriteEvent(EvtRerankFailing, source, ex_type, ex_message, hr, consecutive);
    }

    [Event(EvtRerankRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Contextual correction recovered | failures={0}")]
    public void RerankRecovered(int failures)
    {
        if (IsEnabled()) WriteEvent(EvtRerankRecovered, failures);
    }

    // Every failed attempt, the first one included, so a run of failures under an
    // open incident keeps its causes.
    [Event(EvtRerankFailureDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rerank failure | source={0} | ex_type={1} | ex_message={2} | hr={3} | consecutive={4}")]
    public void RerankFailureDetail(string source, string ex_type, string ex_message, int hr, int consecutive)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtRerankFailureDetail, source, ex_type, ex_message, hr, consecutive);
    }

    public static class RerankFailureSources
    {
        public const string Inference = "inference";     // the reranker or its scorer threw
        public const string Delivery  = "delivery";      // posting the verdict to the input thread threw
    }

    // The worker thread that owns the native model is up. whole_sentence says
    // the reranker can judge one closed-sentence transaction; caret_verification
    // that a verdict is re-checked against the live caret before it applies.
    [Event(EvtRerankLaneStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "rerank lane started | whole_sentence={0} | caret_verification={1}")]
    public void RerankLaneStarted(bool whole_sentence, bool caret_verification)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRerankLaneStarted, whole_sentence, caret_verification);
    }

    // outcome is completed, cancelled (the normal Dispose path) or failed;
    // duration_ms is how long Dispose waited for an in-flight native inference,
    // the untimed wait that can stretch a shutdown; dropped_requests counts
    // requests the drop-oldest queue replaced before the worker took them.
    [Event(EvtRerankLaneStopped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "rerank lane stopped | outcome={0} | duration_ms={1} | dropped_requests={2}")]
    public void RerankLaneStopped(string outcome, long duration_ms, int dropped_requests)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRerankLaneStopped, outcome, duration_ms, dropped_requests);
    }

    // The worker loop itself died outside any request: the contextual stage is
    // gone for the rest of the session.
    [Event(EvtRerankWorkerFaulted,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Contextual correction worker stopped on an error | {0}: {1} | hr={2}")]
    public void RerankWorkerFaulted(string ex_type, string ex_message, int hr)
    {
        if (IsEnabled()) WriteEvent(EvtRerankWorkerFaulted, ex_type, ex_message, hr);
    }

    // The background caret read behind sentence recovery threw (UI Automation
    // provider failure). Opened once, closed by the next read that returns.
    [Event(EvtCaretRecoveryFailing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Caret sentence recovery is failing | {0}: {1}")]
    public void CaretRecoveryFailing(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCaretRecoveryFailing, ex_type, ex_message);
    }

    [Event(EvtCaretRecoveryRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Caret sentence recovery recovered | failures={0}")]
    public void CaretRecoveryRecovered(int failures)
    {
        if (IsEnabled()) WriteEvent(EvtCaretRecoveryRecovered, failures);
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
    // Once per process name per session: a milestone, not activity detail, so
    // it stays out of the activity admission gate.
    public void EnrollmentSuggested(string process)
    {
        if (IsEnabled()) WriteEvent(EvtEnrollmentSuggested, process);
    }
}
