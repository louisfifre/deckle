using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Input.Autocorrect;

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

    // ── Surface gate ─────────────────────────────────────────────────────

    [Event(EvtSurfaceChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "surface | process={0} | editable={1} | password={2} | enrolled={3}")]
    public void SurfaceChanged(string process, bool editable, bool password, bool enrolled)
    {
        if (IsEnabled()) WriteEvent(EvtSurfaceChanged, process, editable, password, enrolled);
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
}
