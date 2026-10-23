using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell.Foreground;

// Foreground watcher provider: watcher lifecycle, hook failures, and one
// detail line per foreground switch or fullscreen change. Location changes
// themselves are never logged — they arrive at drag cadence and only the
// rare publish they cause carries diagnostic value.
[EventSource(Name = "Deckle-Foreground")]
public sealed class DeckleShellForegroundSource : DeckleEventSource
{
    public static readonly DeckleShellForegroundSource Log = new();

    private DeckleShellForegroundSource() { }

    private bool IsWindowingDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Windowing, this, level, keywords);

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // Sequential from 1. IDs are public in the ETW manifest; do not reuse an
    // ID after deleting an event.
    public const int EvtWatcherStarted     = 1;
    public const int EvtWatcherStopped     = 2;
    // 3 retired.
    // 4 retired.
    public const int EvtForegroundChanged  = 5;
    public const int EvtHookFailed         = 6;
    public const int EvtHookFailedDetail   = 7;
    public const int EvtHookRecovered      = 8;
    public const int EvtWatcherStartFailed = 9;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Event(EvtWatcherStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Watcher started")]
    public void WatcherStarted()
    {
        if (IsEnabled()) WriteEvent(EvtWatcherStarted);
    }

    // reason: foreground_hook_failed, the only way Start returns false.
    // SetWinEventHook documents no GetLastError, so win32_error may read 0.
    [Event(EvtWatcherStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Watcher failed to start, fullscreen detection is off | reason={0} | win32_error={1}")]
    public void WatcherStartFailed(string reason, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtWatcherStartFailed, reason, win32_error);
    }

    [Event(EvtWatcherStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Watcher stopped")]
    public void WatcherStopped()
    {
        if (IsEnabled()) WriteEvent(EvtWatcherStopped);
    }

    // ── Failures ──────────────────────────────────────────────────────────────

    // One Warning per incident: the location hook is re-armed on every
    // foreground switch, so its failure is reported once until a later arm
    // succeeds (HookRecovered); every failed attempt lands in the Verbose
    // mirror. win32_error is the attempt that opened the incident.
    [Event(EvtHookFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A foreground hook failed — fullscreen detection is degraded | hook={0} | win32_error={1}")]
    public void HookFailed(string hook, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtHookFailed, hook, win32_error);
    }

    [Event(EvtHookFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hook failed | hook={0} | process_id={1} | win32_error={2}")]
    public void HookFailedDetail(string hook, long process_id, int win32_error)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtHookFailedDetail, hook, process_id, win32_error);
    }

    [Event(EvtHookRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Foreground hook armed again, fullscreen detection is back | hook={0}")]
    public void HookRecovered(string hook)
    {
        if (IsEnabled()) WriteEvent(EvtHookRecovered, hook);
    }

    // ── Foreground ────────────────────────────────────────────────────────────

    [Event(EvtForegroundChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "foreground | process_name={0} | fullscreen={1} | reason={2}")]
    public void ForegroundChanged(string process_name, string fullscreen, string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtForegroundChanged, process_name, fullscreen, reason);
    }
}
