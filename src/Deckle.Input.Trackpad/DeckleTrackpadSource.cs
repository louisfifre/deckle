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

    // 1 retired.
    // 2 retired.
    // 3 retired.
    public const int EvtDragStarted              = 4;
    public const int EvtDragEnded                = 5;
    public const int EvtTapIgnored               = 6;
    // 7 retired.
    public const int EvtGesturesNeutralized      = 8;
    public const int EvtGesturesNeutralizedDetail = 9;
    public const int EvtGesturesRestored         = 10;
    // 11 retired.
    public const int EvtRepairLaunched           = 12;
    // 13 retired.
    // 14 retired.
    // 15 retired.
    // 16 retired.
    // 17 retired.
    public const int EvtEngineStarted            = 18;
    public const int EvtEngineStopped            = 19;
    public const int EvtInjectionFailed          = 20;
    public const int EvtInjectionIncidentEnded   = 21;
    public const int EvtGestureWriteFailed       = 22;
    public const int EvtGestureStateUnreadable   = 23;
    public const int EvtRepairLaunchFailed       = 24;
    public const int EvtRepairCompleted          = 25;
    public const int EvtRepairFailed             = 26;
    public const int EvtActFailed                = 27;
    public const int EvtRepairExitUnobserved     = 28;
    public const int EvtPrimaryReleaseFailed     = 29;
    public const int EvtSettingsLoadWarning = 30;
    public const int EvtSettingsLoadError = 31;

    // ── Engine lifecycle ─────────────────────────────────────────────────

    // touchpad=false means the engine is armed but no Precision Touchpad was
    // known at start: drags will only begin once one connects.
    [Event(EvtEngineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Three-finger drag enabled | drag_speed={0} | touchpad={1}")]
    public void EngineStarted(double drag_speed, bool touchpad)
    {
        if (IsEnabled()) WriteEvent(EvtEngineStarted, drag_speed, touchpad);
    }

    [Event(EvtEngineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Three-finger drag disabled | outcome={0} | drags={1}")]
    public void EngineStopped(string outcome, long drags)
    {
        if (IsEnabled()) WriteEvent(EvtEngineStopped, outcome, drags);
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

    // Opens the injection incident; later failures stay quiet until
    // InjectionIncidentEnded. win32_error 5 against an elevated foreground
    // window is UIPI refusing SendInput.
    [Event(EvtInjectionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Mouse injection failed | action={0} | win32_error={1}")]
    public void InjectionFailed(string action, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailed, action, win32_error);
    }

    // The synthetic primary button could not be released, so Windows may still
    // see it held. Written for each such drag end, also while the injection
    // incident is open: the incident's opener names the first failing action,
    // and a release is the one whose failure outlasts the drag.
    [Event(EvtPrimaryReleaseFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "The mouse button could not be released after a drag | win32_error={0}")]
    public void PrimaryReleaseFailed(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtPrimaryReleaseFailed, win32_error);
    }

    // reason: injection_succeeded (the path recovered) or engine_stopped (the
    // engine ended the incident, not a recovery). The journal keeps the
    // Warning, so it keeps its end too.
    [Event(EvtInjectionIncidentEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Journal,
           Message = "Mouse injection incident ended | reason={0}")]
    public void InjectionIncidentEnded(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionIncidentEnded, reason);
    }

    // ── Acts ─────────────────────────────────────────────────────────────

    [Event(EvtGesturesNeutralized,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Journal,
           Message = "Windows three-finger gestures neutralized")]
    public void GesturesNeutralized()
    {
        if (IsEnabled()) WriteEvent(EvtGesturesNeutralized);
    }

    [Event(EvtGesturesNeutralizedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "gesture backup saved | backup_values={0}")]
    public void GesturesNeutralizedDetail(string backup_values)
    {
        if (IsEnabled()) WriteEvent(EvtGesturesNeutralizedDetail, backup_values);
    }

    [Event(EvtGesturesRestored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Journal,
           Message = "Windows three-finger gestures restored")]
    public void GesturesRestored()
    {
        if (IsEnabled()) WriteEvent(EvtGesturesRestored);
    }

    // operation: neutralize | restore. reason: key_unavailable,
    // backup_malformed, or write_threw (ex_type/ex_message then say why).
    [Event(EvtGestureWriteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Gesture registry write failed | operation={0} | reason={1} | ex_type={2} | ex_message={3}")]
    public void GestureWriteFailed(string operation, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtGestureWriteFailed, operation, reason, ex_type, ex_message);
    }

    // query: are_neutralized | has_backup. The page then shows the safe "off"
    // reading, which may not match what Windows actually has.
    [Event(EvtGestureStateUnreadable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Gesture state unreadable | query={0} | ex_type={1} | ex_message={2}")]
    public void GestureStateUnreadable(string query, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtGestureStateUnreadable, query, ex_type, ex_message);
    }

    [Event(EvtRepairLaunched,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Connection repair script launched")]
    public void RepairLaunched()
    {
        if (IsEnabled()) WriteEvent(EvtRepairLaunched);
    }

    // reason: uac_declined (win32_error 1223) or launch_failed.
    [Event(EvtRepairLaunchFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Connection repair script failed to launch | reason={0} | win32_error={1} | ex_type={2} | ex_message={3}")]
    public void RepairLaunchFailed(string reason, int win32_error, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRepairLaunchFailed, reason, win32_error, ex_type, ex_message);
    }

    [Event(EvtRepairCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Connection repair script completed | exit_code={0}")]
    public void RepairCompleted(int exit_code)
    {
        if (IsEnabled()) WriteEvent(EvtRepairCompleted, exit_code);
    }

    // reason: script_failed (the script's own exit 1 after a failed step) or
    // script_interrupted (any other code, typically the console closed).
    [Event(EvtRepairFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Connection repair script failed | reason={0} | exit_code={1}")]
    public void RepairFailed(string reason, int exit_code)
    {
        if (IsEnabled()) WriteEvent(EvtRepairFailed, reason, exit_code);
    }

    // The repair runs regardless; only its outcome line is lost.
    [Event(EvtRepairExitUnobserved,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "repair exit unobserved | ex_type={0} | ex_message={1}")]
    public void RepairExitUnobserved(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRepairExitUnobserved, ex_type, ex_message);
    }

    // act: gestures | repair. The page swallowed the exception; the act did
    // not run, so what the user sees did not change.
    [Event(EvtActFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Trackpad act failed | act={0} | ex_type={1} | ex_message={2}")]
    public void ActFailed(string act, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtActFailed, act, ex_type, ex_message);
    }

    // The settings store reports in free text; the text carries its own cause.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The trackpad settings need attention | {0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The trackpad settings could not be saved | {0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }
}
