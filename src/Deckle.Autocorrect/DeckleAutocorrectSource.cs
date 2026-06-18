using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Autocorrect;

// Autocorrect module provider. Covers the engine lifecycle, surface
// transitions, applied/reverted corrections and learning signals.
//
// The default events carry counts, lengths and reasons only — no typed text on
// the always-on path. A few opt-in, consent-gated events deliberately carry words
// (the per-word decision and reranker records; the typed-sentence corpus), routed
// to dedicated datasets and off by default.
[EventSource(Name = "Deckle-Autocorrect")]
public sealed class DeckleAutocorrectSource : DeckleEventSource
{
    public static readonly DeckleAutocorrectSource Log = new();

    private DeckleAutocorrectSource() { }

    public const int EvtEngineStarted      = 1;
    public const int EvtEngineStopped      = 2;
    public const int EvtSurfaceChanged     = 3;
    public const int EvtCorrectionApplied  = 4;
    public const int EvtCorrectionDetail   = 5;
    public const int EvtCorrectionReverted = 6;
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
    public const int EvtAutocorrectRevert   = 20;

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

    // load_ms is the wall-clock of the off-UI-thread lexicon+index+bigram
    // build (gzip decode + dictionary build of the multi-MB FR frequency
    // data); entries is the loaded FR lexicon form count. Structured-verbose
    // sibling of the EngineReady milestone — mirrors whisper's
    // ModelLoadComplete(load_ms, backend). Verbose so the number never reaches
    // the Info milestone stream.
    [Event(EvtLexiconLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "lexicon load complete | load_ms={0} | entries={1}")]
    public void LexiconLoadComplete(long load_ms, int entries)
    {
        if (IsEnabled()) WriteEvent(EvtLexiconLoadComplete, load_ms, entries);
    }

    // Concise readiness edge: the engine is built, wired and reconciled to the
    // persisted settings. No number — its verbose sibling LexiconLoadComplete
    // carries the timing. Mirrors whisper's ModelLoaded() "Model loaded".
    [Event(EvtEngineReady,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autocorrect ready")]
    public void EngineReady()
    {
        if (IsEnabled()) WriteEvent(EvtEngineReady);
    }

    // Whether the contextual (CamemBERT) stage came up: true when its model was
    // present and loaded, false when the engine runs gate + typo only.
    [Event(EvtRerankerStatus,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autocorrect contextual stage | model_present={0}")]
    public void RerankerStatus(bool model_present)
    {
        if (IsEnabled()) WriteEvent(EvtRerankerStatus, model_present);
    }

    // ── Surface gate ─────────────────────────────────────────────────────

    [Event(EvtSurfaceChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "surface | process={0} | editable={1} | password={2} | enrolled={3} | {4}")]
    public void SurfaceChanged(string process, bool editable, bool password, bool enrolled, string probe)
    {
        if (IsEnabled()) WriteEvent(EvtSurfaceChanged, process, editable, password, enrolled, probe);
    }

    // ── Corrections ──────────────────────────────────────────────────────

    [Event(EvtCorrectionApplied,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Corrected a word")]
    public void CorrectionApplied()
    {
        if (IsEnabled()) WriteEvent(EvtCorrectionApplied);
    }

    [Event(EvtCorrectionDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "correction | reason={0} | original_len={1} | replacement_len={2} | backspaces={3}")]
    public void CorrectionDetail(string reason, int original_len, int replacement_len, int backspaces)
    {
        if (IsEnabled()) WriteEvent(EvtCorrectionDetail, reason, original_len, replacement_len, backspaces);
    }

    [Event(EvtCorrectionReverted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Correction reverted")]
    public void CorrectionReverted()
    {
        if (IsEnabled()) WriteEvent(EvtCorrectionReverted);
    }

    [Event(EvtInjectionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "correction injection failed | backspaces={0} | text_len={1}")]
    public void InjectionFailed(int backspaces, int text_len)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailed, backspaces, text_len);
    }

    // ── Contextual reranker decisions ─────────────────────────────────────
    //
    // The slot's life across the deferred second stage, so a "why didn't it
    // correct?" reads off the trace: a SlotPending with no Submitted means the
    // right-context deferral was never met; a Submitted with no Verdict means the
    // inference is still running or died; an abstain/stale Verdict says the model
    // declined or the sentence was reset under it. Counts and a closed outcome
    // vocabulary only — no typed text on these pipeline events.

    // A real-word ambiguity the synchronous gate left intact is now an open slot,
    // waiting for enough right-context (or a sentence-ender) before the CamemBERT
    // reranker decides it. candidates is the closed candidate-set size.
    [Event(EvtRerankSlotPending,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rerank slot pending | candidates={0} | word_len={1}")]
    public void RerankSlotPending(int candidates, int word_len)
    {
        if (IsEnabled()) WriteEvent(EvtRerankSlotPending, candidates, word_len);
    }

    // The slot crossed the deferral threshold (or a sentence-ender flushed it)
    // and was handed to the background reranker. slot is its index within the
    // submitted window; context_words the window size around it.
    [Event(EvtRerankSubmitted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rerank submitted | slot={0} | context_words={1}")]
    public void RerankSubmitted(int slot, int context_words)
    {
        if (IsEnabled()) WriteEvent(EvtRerankSubmitted, slot, context_words);
    }

    // The reranker verdict landed. outcome is a closed vocabulary: applied (slot
    // rewritten), equal (model chose the typed form), abstain (model not
    // confident — left as typed), stale (the sentence was reset under the
    // in-flight request), resolved (slot already decided), blocked (the in-place
    // rewrite was refused by the target surface).
    [Event(EvtRerankVerdict,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rerank verdict | outcome={0}")]
    public void RerankVerdict(string outcome)
    {
        if (IsEnabled()) WriteEvent(EvtRerankVerdict, outcome);
    }

    // ── Correction decision dataset ──────────────────────────────────────
    //
    // The per-word decision ledger — the deep "why did(n't) it correct?" surface.
    // Unlike every event above (counts, lengths and a closed outcome vocabulary
    // only), these two DELIBERATELY carry text: the typed word, its left context,
    // and the candidate forms with their scores. That is the whole point — to see
    // the behaviour fully before adding any new correction. They are routed solely
    // to the dedicated, opt-in autocorrect.decisions.jsonl telemetry sink (gated by
    // AutocorrectDecisionsEnabled, off by default) and excluded from app.jsonl; no
    // other path reads them. The synchronous decision and its deferred reranker
    // verdict share `id`, so the two lines join.
    //
    // Fields are flat and self-reading (no re-encoded JSON): candidates as
    // "form@freq@source", gauges as "name=value" with their "…_min/…_max" bounds,
    // trail as "stage:reason" across the chain. CorrectionTrace renders them.

    [Event(EvtAutocorrectDecision,
           Level = EventLevel.Verbose,
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

    // The revert gesture as a per-word record, joined to the correction it undoes
    // by `id` — the same monotonic word id AutocorrectDecisionRecorded carried, so
    // the revert line sits beside the decision that fired. Carries the pair (the
    // literal restored, the correction undone) and the boundary the Backspace
    // consumed: its kind buckets the known misfire — a `punctuation` boundary is
    // the user deleting a misplaced comma/period right after a correction, misread
    // as an undo, where a `whitespace` boundary is the plausible genuine "I didn't
    // want that". delta_ms is the gap from the correction commit to the revert;
    // outcome is restored (the literal landed) or desynced (the rewrite did not).
    // Text by design like its sibling decision events — routed to the same opt-in
    // autocorrect.decisions dataset, off by default, and excluded from app.jsonl.
    [Event(EvtAutocorrectRevert,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "revert | {1} ← {2} | {4} | {6}")]
    public void AutocorrectRevertRecorded(
        long   id,
        string original,
        string replacement,
        string boundary,
        string boundaryKind,
        long   delta_ms,
        string outcome)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtAutocorrectRevert,
            id, original, replacement, boundary, boundaryKind, delta_ms, outcome);
    }

    // ── Typed-sentence corpus ─────────────────────────────────────────────
    //
    // One sentence the user typed on an enrolled surface, as two parallel strings:
    // `typed` verbatim (keyboard substitutions and all — the telling ';' for an
    // apostrophe survives) and `final` after the corrector. Feeds the per-user
    // error-pattern corpus; routed to the dedicated, opt-in autocorrect.text.jsonl
    // sink (gated by AutocorrectText, off by default) and excluded from app.jsonl.
    // The heaviest text capture in the module — a verbatim record of typed input —
    // so its consent toggle stands on its own.

    [Event(EvtAutocorrectText,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "text | {0} | {1}")]
    public void AutocorrectTextRecorded(string process, string typed, string final)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtAutocorrectText, process, typed, final);
    }

    // ── Learning ─────────────────────────────────────────────────────────

    [Event(EvtLearningSignal,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "learning | signal={0}")]
    public void LearningSignal(string signal)
    {
        if (IsEnabled()) WriteEvent(EvtLearningSignal, signal);
    }

    // ── Activity rollup (30 s aggregate while words commit) ──────────────

    [Event(EvtActivityRollup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "autocorrect activity | commits={0} | corrections={1} | reverts={2} | learning_signals={3} | gated_surfaces={4}")]
    public void ActivityRollup(int commits, int corrections, int reverts, int learning_signals, int gated_surfaces)
    {
        if (IsEnabled()) WriteEvent(EvtActivityRollup, commits, corrections, reverts, learning_signals, gated_surfaces);
    }

    // ── Enrollment ───────────────────────────────────────────────────────

    [Event(EvtEnrollmentSuggested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "enrollment suggested | process={0}")]
    public void EnrollmentSuggested(string process)
    {
        if (IsEnabled()) WriteEvent(EvtEnrollmentSuggested, process);
    }
}
