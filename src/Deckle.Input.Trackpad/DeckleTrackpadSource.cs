using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Input.Trackpad;

// Trackpad module provider. Covers the three-finger drag engine
// lifecycle, per-gesture outcomes (drag spans, ignored taps), injection
// failures, and the one-click acts (Windows gesture neutralization,
// connection repair).
//
// Per-gesture events are Verbose: a drag is a human-rate operation but
// still far too frequent for the Activity view; the Info milestones are
// the engine start/stop and the acts.
[EventSource(Name = "Deckle-Trackpad")]
public sealed class DeckleTrackpadSource : DeckleEventSource
{
    public static readonly DeckleTrackpadSource Log = new();

    private DeckleTrackpadSource() { }

    public const int EvtEngineStarted            = 1;
    public const int EvtEngineStopped            = 2;
    public const int EvtTuningApplied            = 3;
    public const int EvtDragStarted              = 4;
    public const int EvtDragEnded                = 5;
    public const int EvtTapIgnored               = 6;
    public const int EvtInjectionFailed          = 7;
    public const int EvtGesturesNeutralized      = 8;
    public const int EvtGesturesNeutralizedDetail = 9;
    public const int EvtGesturesRestored         = 10;
    public const int EvtGestureWriteFailed       = 11;
    public const int EvtRepairLaunched           = 12;
    public const int EvtRepairLaunchFailed       = 13;

    // ── Engine lifecycle ─────────────────────────────────────────────────

    [Event(EvtEngineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Three-finger drag enabled")]
    public void EngineStarted()
    {
        if (IsEnabled()) WriteEvent(EvtEngineStarted);
    }

    [Event(EvtEngineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Three-finger drag disabled")]
    public void EngineStopped()
    {
        if (IsEnabled()) WriteEvent(EvtEngineStopped);
    }

    [Event(EvtTuningApplied,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tuning | grace_ms={0} | start_threshold_units={1} | base_scale={2} | speed={3}")]
    public void TuningApplied(int grace_ms, double start_threshold_units, double base_scale, double speed)
    {
        if (IsEnabled()) WriteEvent(EvtTuningApplied, grace_ms, start_threshold_units, base_scale, speed);
    }

    // ── Gestures ─────────────────────────────────────────────────────────

    [Event(EvtDragStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "drag started")]
    public void DragStarted()
    {
        if (IsEnabled()) WriteEvent(EvtDragStarted);
    }

    [Event(EvtDragEnded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "drag ended | reason={0} | duration_ms={1} | moves={2}")]
    public void DragEnded(string reason, double duration_ms, int moves)
    {
        if (IsEnabled()) WriteEvent(EvtDragEnded, reason, duration_ms, moves);
    }

    [Event(EvtTapIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "three-finger tap ignored")]
    public void TapIgnored()
    {
        if (IsEnabled()) WriteEvent(EvtTapIgnored);
    }

    [Event(EvtInjectionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "injection failed | action={0} | win32_error={1}")]
    public void InjectionFailed(string action, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailed, action, win32_error);
    }

    // ── Acts ─────────────────────────────────────────────────────────────

    [Event(EvtGesturesNeutralized,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Windows three-finger gestures neutralized")]
    public void GesturesNeutralized()
    {
        if (IsEnabled()) WriteEvent(EvtGesturesNeutralized);
    }

    [Event(EvtGesturesNeutralizedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "gesture backup | {0}")]
    public void GesturesNeutralizedDetail(string backup_values)
    {
        if (IsEnabled()) WriteEvent(EvtGesturesNeutralizedDetail, backup_values);
    }

    [Event(EvtGesturesRestored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Windows three-finger gestures restored")]
    public void GesturesRestored()
    {
        if (IsEnabled()) WriteEvent(EvtGesturesRestored);
    }

    [Event(EvtGestureWriteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "gesture registry write failed | error={0}: {1}")]
    public void GestureWriteFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtGestureWriteFailed, ex_type, message);
    }

    [Event(EvtRepairLaunched,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Connection repair script launched")]
    public void RepairLaunched()
    {
        if (IsEnabled()) WriteEvent(EvtRepairLaunched);
    }

    [Event(EvtRepairLaunchFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "connection repair launch failed | error={0}: {1}")]
    public void RepairLaunchFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtRepairLaunchFailed, ex_type, message);
    }
}
