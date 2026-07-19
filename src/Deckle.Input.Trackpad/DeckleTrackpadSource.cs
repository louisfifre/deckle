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

    internal static bool IsInputActivityDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Input, Log, level, keywords);

    public const int EvtEngineStarted            = 1;
    public const int EvtEngineStopped            = 2;
    // 3 retired — TuningApplied, removed at the value freeze (2026-06-12).
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
    // Verbose mirrors added for the Verbose/Info separation (2026-06-13):
    // InjectionFailed, GestureWriteFailed, and RepairLaunchFailed carried
    // k=v detail at Warning level; the detail moves to these fresh mirrors.
    public const int EvtInjectionFailedDetail    = 14;
    public const int EvtGestureWriteFailedDetail = 15;
    public const int EvtRepairLaunchFailedDetail = 16;
    public const int EvtInjectionRecovered       = 17;

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

    // ── Gestures ─────────────────────────────────────────────────────────

    [Event(EvtDragStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "drag started")]
    public void DragStarted()
    {
        if (IsInputActivityDetailEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtDragStarted);
    }

    [Event(EvtDragEnded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "drag ended | reason={0} | duration_ms={1} | moves={2}")]
    public void DragEnded(string reason, double duration_ms, int moves)
    {
        if (IsInputActivityDetailEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtDragEnded, reason, duration_ms, moves);
    }

    [Event(EvtTapIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "three-finger tap ignored")]
    public void TapIgnored()
    {
        if (IsInputActivityDetailEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtTapIgnored);
    }

    [Event(EvtInjectionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Mouse injection failed")]
    public void InjectionFailed()
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailed);
    }

    [Event(EvtInjectionFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "injection failed | action={0} | win32_error={1}")]
    public void InjectionFailedDetail(string action, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailedDetail, action, win32_error);
    }

    [Event(EvtInjectionRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Mouse injection recovered")]
    public void InjectionRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtInjectionRecovered);
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
           Message = "Gesture registry write failed")]
    public void GestureWriteFailed()
    {
        if (IsEnabled()) WriteEvent(EvtGestureWriteFailed);
    }

    [Event(EvtGestureWriteFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "gesture registry write failed | ex_type={0} | message={1}")]
    public void GestureWriteFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtGestureWriteFailedDetail, ex_type, message);
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
           Message = "Connection repair script failed to launch")]
    public void RepairLaunchFailed()
    {
        if (IsEnabled()) WriteEvent(EvtRepairLaunchFailed);
    }

    [Event(EvtRepairLaunchFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "connection repair launch failed | ex_type={0} | message={1}")]
    public void RepairLaunchFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtRepairLaunchFailedDetail, ex_type, message);
    }
}
