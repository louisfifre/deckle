using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell.Foreground;

// Foreground watcher provider: watcher lifecycle, hook failures, and one
// detail line per foreground switch or fullscreen change. Location changes
// themselves are never logged — they arrive at drag cadence and only the
// rare publish they cause carries diagnostic value.
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info or a
// Warning is a short Capital sentence with no IDs and no k=v; the technical
// detail lives in a Verbose mirror that FOLLOWS it.
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
    public const int EvtHookFailed         = 3;
    public const int EvtHookFailedDetail   = 4;
    public const int EvtForegroundChanged  = 5;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Event(EvtWatcherStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Watcher started")]
    public void WatcherStarted()
    {
        if (IsEnabled()) WriteEvent(EvtWatcherStarted);
    }

    [Event(EvtWatcherStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Watcher stopped")]
    public void WatcherStopped()
    {
        if (IsEnabled()) WriteEvent(EvtWatcherStopped);
    }

    // ── Failures ──────────────────────────────────────────────────────────────

    // One Warning per incident: the location hook is re-armed on every
    // foreground switch, so its failure is reported once until a later arm
    // succeeds; every failed attempt still lands in the Verbose mirror.
    [Event(EvtHookFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A foreground hook failed — fullscreen detection is degraded")]
    public void HookFailed()
    {
        if (IsEnabled()) WriteEvent(EvtHookFailed);
    }

    [Event(EvtHookFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hook failed | hook={0} | process_id={1}")]
    public void HookFailedDetail(string hook, long process_id)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtHookFailedDetail, hook, process_id);
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
