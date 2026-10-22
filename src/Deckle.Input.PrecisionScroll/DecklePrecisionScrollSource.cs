using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Input.PrecisionScroll;

[EventSource(Name = "Deckle-PrecisionScroll")]
public sealed class DecklePrecisionScrollSource : DeckleEventSource
{
    public static readonly DecklePrecisionScrollSource Log = new();

    private DecklePrecisionScrollSource() { }

    // 1 retired.
    // 2 retired.
    public const int EvtEngineStoppedDetail = 3;
    // 4 retired.
    // 5 retired.
    // 6 retired.
    // 7 retired.
    // 8 retired.
    // 9 retired.
    // 10 retired.
    public const int EvtEngineStarted = 11;
    public const int EvtEngineStartedDetail = 12;
    public const int EvtEngineStopped = 13;
    public const int EvtUnavailable = 14;
    public const int EvtTouchpadSettingsUnavailable = 15;
    public const int EvtInjectionFailed = 16;
    public const int EvtWorkerFaulted = 17;
    public const int EvtQueueOverloaded = 18;
    public const int EvtInactive = 19;
    public const int EvtStopSlow = 20;
    public const int EvtSettingsLoadWarning = 21;
    public const int EvtSettingsLoadError = 22;
    public const int EvtRestartScheduledDetail = 23;
    public const int EvtFellBackToNativeWheel = 24;
    public const int EvtFallbackRecovered = 25;

    [Event(EvtEngineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Precision scrolling enabled | direction_reversed={0} | mouse_hook={1}")]
    public void EngineStarted(bool direction_reversed, bool mouse_hook)
    {
        if (IsEnabled()) WriteEvent(EvtEngineStarted, direction_reversed, mouse_hook);
    }

    [Event(EvtEngineStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "precision scrolling tuning | distance_per_detent_mm={0} | initial_step_duration_ms={1} | quiet_period_scale={2} | queue_capacity={3}")]
    public void EngineStartedDetail(
        double distance_per_detent_mm,
        double initial_step_duration_ms,
        double quiet_period_scale,
        int queue_capacity)
    {
        if (IsEnabled())
            WriteEvent(EvtEngineStartedDetail,
                distance_per_detent_mm, initial_step_duration_ms, quiet_period_scale, queue_capacity);
    }

    // outcome is completed when the setting turned the engine off, failed
    // when the worker ended on its own; reason then names why (injection_failed,
    // worker_faulted, queue_full) and stays empty for a requested stop.
    [Event(EvtEngineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Precision scrolling disabled | outcome={0} | reason={1}")]
    public void EngineStopped(string outcome, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtEngineStopped, outcome, reason);
    }

    [Event(EvtEngineStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "precision scrolling closed | detents={0} | gestures={1} | rollovers={2}")]
    public void EngineStoppedDetail(long detents, long gestures, long rollovers)
    {
        if (IsEnabled()) WriteEvent(EvtEngineStoppedDetail, detents, gestures, rollovers);
    }

    // reason: create_failed, api_missing or thread_start_failed. The exception
    // fields stay empty when a Win32 call reported the failure.
    [Event(EvtUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Precision scrolling is unavailable | reason={0} | win32_error={1} | ex_type={2} | ex_message={3}")]
    public void Unavailable(string reason, int win32_error, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtUnavailable, reason, win32_error, ex_type, ex_message);
    }

    [Event(EvtTouchpadSettingsUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Touchpad direction could not be read | win32_error={0}")]
    public void TouchpadSettingsUnavailable(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtTouchpadSettingsUnavailable, win32_error);
    }

    // Error, not Warning: a refused frame ends the worker and the wheel goes
    // native. PrecisionScrollRuntime then restarts it once; when that one stops
    // too, FellBackToNativeWheel says it stays off.
    [Event(EvtInjectionFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Touchpad injection failed, precision scrolling stopped | action={0} | win32_error={1}")]
    public void InjectionFailed(string action, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailed, action, win32_error);
    }

    [Event(EvtWorkerFaulted,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Precision scrolling stopped on an unexpected error | ex_type={0} | ex_message={1}")]
    public void WorkerFaulted(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWorkerFaulted, ex_type, ex_message);
    }

    [Event(EvtQueueOverloaded,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Precision scrolling stopped to protect input latency | reason={0} | queue_capacity={1}")]
    public void QueueOverloaded(string reason, int queue_capacity)
    {
        if (IsEnabled()) WriteEvent(EvtQueueOverloaded, reason, queue_capacity);
    }

    // The engine runs but cannot see a suppressible wheel: without the
    // low-level hook, every detent stays native.
    [Event(EvtInactive,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Precision scrolling cannot intercept the wheel | reason={0}")]
    public void Inactive(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtInactive, reason);
    }

    [Event(EvtStopSlow,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Precision scrolling is slow to stop | timeout_ms={0}")]
    public void StopSlow(int timeout_ms)
    {
        if (IsEnabled()) WriteEvent(EvtStopSlow, timeout_ms);
    }

    // The worker ended on its own and the runtime will start it again once,
    // after delay_ms. reason is the engine's: queue_full, injection_failed or
    // worker_faulted. The engine's own line already told the journal why it ended.
    [Event(EvtRestartScheduledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "precision scrolling restart scheduled | reason={0} | delay_ms={1}")]
    public void RestartScheduledDetail(string reason, long delay_ms)
    {
        if (IsEnabled()) WriteEvent(EvtRestartScheduledDetail, reason, delay_ms);
    }

    // Opens the incident "the wheel is native although the switch is on": the
    // restart did not hold or could not start. It stays down, with no further
    // attempt, until the person toggles the switch. reason is the engine's
    // (queue_full, injection_failed, worker_faulted) or restart_failed when the
    // restart itself could not start.
    [Event(EvtFellBackToNativeWheel,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Precision scrolling stopped again and stays off, the wheel scrolls natively | reason={0}")]
    public void FellBackToNativeWheel(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtFellBackToNativeWheel, reason);
    }

    // Closes the incident FellBackToNativeWheel opened, when the person
    // switches the feature off. Only that closes it: once the wheel stays
    // native nothing restarts the engine, so no start can end the incident
    // before the person acts.
    [Event(EvtFallbackRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Precision scrolling no longer stays off, the person toggled it | previous_reason={0}")]
    public void FallbackRecovered(string previous_reason)
    {
        if (IsEnabled()) WriteEvent(EvtFallbackRecovered, previous_reason);
    }

    // The settings store reports in free text; the text carries its own cause.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The precision scrolling settings need attention | {0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The precision scrolling settings could not be saved | {0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }
}
