using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Autocorrect;

// Autocorrect module provider. Covers the engine lifecycle, surface
// transitions, applied corrections and learning signals.
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

    internal static bool IsActivityDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Autocorrect, Log, level, keywords);

    // Ids 6 (CorrectionReverted) and 20 (AutocorrectRevertRecorded) are retired
    // with the implicit-Backspace revert — never reuse them, old logs carry them.
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
    public const int EvtPausePassTriggered  = 22;
    public const int EvtInjectionIncident   = 23;
    public const int EvtInjectionRecovered  = 24;
    public const int EvtInjectionEpisodeDetail = 25;
    public const int EvtRerankerLoadFailed  = 26;

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

    // Which contextual (sentence-stage) engine came up and what its model load
    // cost: engine names the winner of the composition-root preference order
    // (RerankerEngines vocabulary), load_ms its wall-clock load. "none" means
    // the engine runs gate + typo only, with deterministic sentence rules.
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

    // Closed vocabulary for RerankerStatus.engine — one spelling, one place.
    public static class RerankerEngines
    {
        public const string SentenceJudge = "sentence_judge"; // ONNX GenAI Qwen judge (DirectML)
        public const string Camembert     = "camembert";      // masked-LM reranker
        public const string None          = "none";           // deterministic rules only
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
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtRerankSlotPending, candidates, word_len);
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
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtRerankSubmitted, slot, context_words);
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
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtRerankVerdict, outcome);
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
    // One sentence the user typed at the keyboard on an enrolled, editable,
    // non-password surface, as two parallel strings: `typed`
    // verbatim (keyboard substitutions and all — the telling ';' for an apostrophe
    // survives) and `final` after the corrector — plus `history`, the ordered path of
    // every slot that changed (first-typed then each stage's transition,
    // "#i=typed»commit:…»user:…"), so a commit repair, a sentence-stage rewrite and a
    // manual re-edit are told apart. `closure` says how the run ended — "sentence"
    // (a '.'/'!'/'?' closed it), "enter" (an Enter), or "interrupted" (any other
    // reset cut it short before an ending boundary) — so a partial run can be weighed
    // apart from a clean one. `timing` is the typing rhythm: comma-joined per-slot
    // inter-commit gaps in ms, first slot "0", empty when no timestamps were
    // available. Feeds the per-user error-pattern corpus; routed to the dedicated,
    // opt-in autocorrect.text.jsonl sink (gated by AutocorrectText, off by default)
    // and excluded from app.jsonl. The heaviest text capture in the module — a
    // verbatim record of typed input — so its consent toggle stands on its own.

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
    // One closed run of the typing stream (CONTEXT.md § Typing stream): the
    // verbatim forward flow typed on an ENROLLED correctable surface, segmented
    // at backward repairs. `text` is the run as it landed on screen; `erased`
    // the backspaces that preceded it inside the span; `closure` why it ended
    // ("repair" and "cap" continue the span, "enter"/"navigation"/"escape"/
    // "shortcut"/"delete"/"deadkey"/"pointer"/"focus" end it); `timing` the
    // per-char keystroke gaps in ms. Replaying the runs in order restores the
    // faulty forms as they stood, what was erased, what was retyped — the
    // substrate of mistouch-family mining and of the natural-language corpus.
    // Verbatim typed input: routed solely to the opt-in autocorrect.stream.jsonl
    // sink under the SAME consent envelope as the typed-sentence corpus (gated
    // by AutocorrectText, off by default) and excluded from app.jsonl. Password
    // surfaces never reach the stream — the engine gates them before decoding.

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

    // ── Pause pass ────────────────────────────────────────────────────────

    // A typing pause on a profiled Enter-heavy surface flushed the open
    // ambiguous slots to the sentence stage early (CONTEXT.md § Pause pass).
    // threshold_ms is the surface's calibrated pause bar; slots how many were
    // put in motion. A verdict beaten by Enter shows up as the existing
    // "stale" RerankVerdict — the residue to measure rides these two together.
    [Event(EvtPausePassTriggered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "pause pass | threshold_ms={0} | slots={1}")]
    public void PausePassTriggered(int threshold_ms, int slots)
    {
        if (!IsActivityDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtPausePassTriggered, threshold_ms, slots);
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
