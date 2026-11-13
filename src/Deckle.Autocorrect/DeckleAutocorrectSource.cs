using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Autocorrect;

// The default events carry counts, lengths and reasons only — no typed text on
// the always-on path. A few opt-in, consent-gated events deliberately carry words
// (the per-word decision record, the typed-sentence corpus, the
// typing stream), routed to dedicated datasets and off by default.
[EventSource(Name = "Deckle-Autocorrect")]
public sealed class DeckleAutocorrectSource : DeckleEventSource
{
    public static readonly DeckleAutocorrectSource Log = new();

    private DeckleAutocorrectSource() { }

    internal static bool IsActivityDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Autocorrect, Log, level, keywords);

    // The activity rollup is Autocorrect's summary, admitted from Summary up.
    internal static bool IsActivitySummaryEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsSummaryEnabled(
            OperationalLogActivity.Autocorrect, Log, level, keywords);

    // Ids 6 and 20 are retired: never reuse them, old logs carry them. So are 13-16,
    // 18, 27, 31-41, 44-46 and 51, the contextual sentence stage's events (the judge,
    // its lane, its slots and the caret sentence recovery): they were removed with
    // that stage, and old logs still carry them.
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
    public const int EvtAutocorrectDecision = 17;
    public const int EvtAutocorrectText     = 19;
    public const int EvtAutocorrectStream   = 21;
    // 22 retired.
    // 23 retired.
    public const int EvtInjectionRecovered  = 24;
    public const int EvtInjectionEpisodeDetail = 25;
    // 26 retired.
    public const int EvtPersonalDictionarySanitized = 28;
    public const int EvtExternalMutationBurst = 29;
    public const int EvtDomainPackMerged      = 30;
    public const int EvtEngineStartFailed     = 42;
    public const int EvtInjectionFailing      = 43;
    public const int EvtSystemLanguagesFallback = 47;
    public const int EvtLearningStoreUnreadable = 48;
    public const int EvtLearningStoreLoaded   = 49;
    // 50 retired: the personal dictionary reset on an older schema; the schema
    // version is gone, so nothing resets it any more.
    public const int EvtDomainPackManifestUnreadable = 52;
    public const int EvtSettingsLoadWarning = 53;
    public const int EvtSettingsLoadError = 54;

    // ── Engine lifecycle ─────────────────────────────────────────────────

    [Event(EvtEngineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Autocorrect armed")]
    public void EngineStarted()
    {
        if (IsEnabled()) WriteEvent(EvtEngineStarted);
    }

    [Event(EvtEngineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
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
           Tags = ObservationTags.Milestone,
           Message = "Autocorrect could not start, so typing is not corrected\nreason={0}")]
    public void EngineStartFailed(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtEngineStartFailed, reason);
    }

    public static class EngineStartFailureReasons
    {
        public const string HostStartFailed = "host_start_failed";
    }

    // load_ms is the wall-clock of the off-UI-thread lexical build;
    // entries is the effective lexicon's form count. Verbose so the number never reaches the Info milestone stream.
    [Event(EvtLexiconLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "load_ms={0} | entries={1}")]
    public void LexiconLoadComplete(long load_ms, long entries)
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
           Message = "pack={0} | forms={1}")]
    public void DomainPackMerged(string pack, int forms)
    {
        if (IsEnabled()) WriteEvent(EvtDomainPackMerged, pack, forms);
    }

    // Readiness edge: the engine is built, wired and reconciled to the persisted
    // settings. No number: its verbose sibling LexiconLoadComplete carries the timing.
    [Event(EvtEngineReady,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Autocorrect ready")]
    public void EngineReady()
    {
        if (IsEnabled()) WriteEvent(EvtEngineReady);
    }

    // Count-only confirmation that the lexical admission policy removed learned
    // entries which can no longer be trusted. The words themselves stay solely
    // in the inspectable personal-dictionary file and never enter this log.
    [Event(EvtPersonalDictionarySanitized,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "removed={0}")]
    public void PersonalDictionarySanitized(int removed)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPersonalDictionarySanitized, removed);
    }

    // The Windows language list could not be read, so the lexicon defaults come
    // from the UI culture instead. fallback is the primary-subtag list actually
    // used ("fr,en"), which explains a domain pack that is off by default.
    [Event(EvtSystemLanguagesFallback,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The system language list is unavailable, so the UI language is used\nfallback={0} | ex_type={1} | ex_message={2}")]
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
           Message = "A learning store is unreadable, so its feature stays inert\nstore={0} | reason={1} | ex_type={2} | ex_message={3}")]
    public void LearningStoreUnreadable(string store, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtLearningStoreUnreadable, store, reason, ex_type, ex_message);
    }

    [Event(EvtLearningStoreLoaded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "store={0} | records_read={1} | records_skipped={2}")]
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
           Message = "pack={0} | ex_type={1} | ex_message={2}")]
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
           Message = "process={0} | editable={1} | password={2} | enrolled={3} | probe={4}")]
    public void SurfaceChanged(string process, bool editable, bool password, bool enrolled, string probe)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Capture)) return;
        WriteEvent(EvtSurfaceChanged, process, editable, password, enrolled, probe);
    }

    // ── Corrections ──────────────────────────────────────────────────────

    [Event(EvtCorrectionApplied,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A word was corrected")]
    public void CorrectionApplied()
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtCorrectionApplied);
    }

    [Event(EvtCorrectionDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "reason={0} | original_len={1} | replacement_len={2} | backspaces={3}")]
    public void CorrectionDetail(string reason, int original_len, int replacement_len, int backspaces)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtCorrectionDetail, reason, original_len, replacement_len, backspaces);
    }

    // Opens the injection incident. win32_error is SendInput's last error, and 0
    // is itself a clue: UIPI blocking against an elevated window rejects the
    // burst without setting any error. The line names no app: an app name stays
    // on Verbose detail events, where SurfaceChanged names the focused app.
    [Event(EvtInjectionFailing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Text injection is failing, corrections may not reach this app\nwin32_error={0}")]
    public void InjectionFailing(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailing, win32_error);
    }

    [Event(EvtInjectionRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "Text injection recovered")]
    public void InjectionRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtInjectionRecovered);
    }

    [Event(EvtInjectionEpisodeDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "outcome={0} | failures={1} | backspaces={2} | text_len={3}")]
    public void InjectionEpisodeDetail(
        string outcome, int failures, int backspaces, int text_len)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtInjectionEpisodeDetail, outcome, failures, backspaces, text_len);
    }

    [Event(EvtExternalMutationBurst,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "outcome={0} | backspaces={1} | text_units={2}")]
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
    // default) and excluded from trace.jsonl. CorrectionTrace renders the flat fields.

    [Event(EvtAutocorrectDecision,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "word={1} | outcome={3} | stage={4} | reason={5}")]
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

    // ── Typed-sentence corpus ─────────────────────────────────────────────
    //
    // One sentence typed on an enrolled, editable, non-password surface; its fields
    // are SentenceCorpus.SentenceRecord's. The heaviest text capture in the module,
    // a verbatim record of typed input: routed solely to the opt-in
    // autocorrect.text.jsonl sink (gated by AutocorrectText, off by default) and
    // excluded from trace.jsonl.

    [Event(EvtAutocorrectText,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "process={0} | typed={1} | closure={4}")]
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
    // default) and excluded from trace.jsonl. Password surfaces never reach the
    // stream: the engine gates them before decoding.

    [Event(EvtAutocorrectStream,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "process={0} | text={1} | erased={2} | closure={3}")]
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
           Message = "signal={0}")]
    public void LearningSignal(string signal)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtLearningSignal, signal);
    }

    // ── Activity rollup (30 s aggregate while words commit) ──────────────

    [Event(EvtActivityRollup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Tags = ObservationTags.Summary,
           Message = "commits={0} | corrections={1} | re_edited={2} | learning_signals={3} | gated_surfaces={4}")]
    public void ActivityRollup(int commits, int corrections, int re_edited, int learning_signals, int gated_surfaces)
    {
        if (!IsActivitySummaryEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtActivityRollup, commits, corrections, re_edited, learning_signals, gated_surfaces);
    }

    // ── Enrollment ───────────────────────────────────────────────────────

    [Event(EvtEnrollmentSuggested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "enrollment suggested")]
    // Once per process name per session: a milestone, not activity detail, so
    // it stays out of the activity admission gate.
    public void EnrollmentSuggested(string process)
    {
        if (IsEnabled()) WriteEvent(EvtEnrollmentSuggested, process);
    }

    // The settings store reports in free text; the text carries its own cause.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The autocorrect settings store worked around a problem\ndetail={0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The autocorrect settings could not be saved or applied\ndetail={0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }
}
