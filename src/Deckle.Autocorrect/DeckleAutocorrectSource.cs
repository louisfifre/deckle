using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Autocorrect;

// Autocorrect module provider. Covers the engine lifecycle, surface
// transitions, applied/reverted corrections and learning signals.
//
// Typed text NEVER crosses this provider (module hard rule): events carry
// counts, lengths and reasons only. The live words are visible solely in
// the CLI console, by explicit dev action.
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
    // vocabulary only — no typed text crosses (module hard rule).

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
