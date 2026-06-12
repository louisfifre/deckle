using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell.TaskbarCover;

// TaskbarCover module provider: host lifecycle, taskbar layout, cover
// visibility transitions and the suppression/suspension gates. Cursor
// movement itself is never logged — the WinEvent stream runs at input
// cadence and only its state transitions (zone enter / re-cover) carry
// diagnostic value; those surface through CoverShown / CoverHidden.
[EventSource(Name = "Deckle-TaskbarCover")]
public sealed class DeckleShellTaskbarCoverSource : DeckleEventSource
{
    public static readonly DeckleShellTaskbarCoverSource Log = new();

    private DeckleShellTaskbarCoverSource() { }

    // ── Event IDs ─────────────────────────────────────────────────────────────
    public const int EvtHostStarted         = 1;
    public const int EvtHostStartFailed     = 2;
    public const int EvtHostStopped         = 3;
    public const int EvtLayoutRebuilt       = 4;
    public const int EvtLayoutQueryFailed   = 5;
    public const int EvtCoverShown          = 6;
    public const int EvtCoverHidden         = 7;
    public const int EvtSuppressionChanged  = 8;
    public const int EvtSystemSuspended     = 9;
    public const int EvtSystemResumed       = 10;
    public const int EvtCursorHookFailed    = 11;
    public const int EvtSessionNotifyFailed = 12;
    public const int EvtHostStopTimedOut    = 13;

    // ── Host lifecycle ────────────────────────────────────────────────────────

    [Event(EvtHostStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "taskbar cover host started | hwnd=0x{0:X} thread_id={1}")]
    public void HostStarted(long hwnd, int thread_id)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHostStarted, hwnd, thread_id);
    }

    [Event(EvtHostStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "taskbar cover host failed to start | error_type={0} message={1}")]
    public void HostStartFailed(string error_type, string message)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHostStartFailed, error_type, message);
    }

    [Event(EvtHostStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "taskbar cover host stopped")]
    public void HostStopped()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHostStopped);
    }

    [Event(EvtHostStopTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "taskbar cover thread did not exit in time — restart refused until it does")]
    public void HostStopTimedOut()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHostStopTimedOut);
    }

    // ── Taskbar layout ────────────────────────────────────────────────────────

    [Event(EvtLayoutRebuilt,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "taskbar layout rebuilt | edge={0} band=({1},{2})-({3},{4}) reason={5}")]
    public void LayoutRebuilt(string edge, int left, int top, int right, int bottom, string reason)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Windowing))
            WriteEvent(EvtLayoutRebuilt, edge, left, top, right, bottom, reason);
    }

    [Event(EvtLayoutQueryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "taskbar position query failed — cover stays hidden until the taskbar is found")]
    public void LayoutQueryFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Windowing))
            WriteEvent(EvtLayoutQueryFailed);
    }

    // ── Cover visibility ──────────────────────────────────────────────────────
    //
    // Verbose: these flip on every approach/retreat of the cursor — many
    // times a day, valuable when diagnosing the state machine, noise in the
    // LogWindow otherwise.

    [Event(EvtCoverShown,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cover shown | reason={0}")]
    public void CoverShown(string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCoverShown, reason);
    }

    [Event(EvtCoverHidden,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cover hidden | reason={0}")]
    public void CoverHidden(string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCoverHidden, reason);
    }

    // ── Gates ─────────────────────────────────────────────────────────────────

    [Event(EvtSuppressionChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "fullscreen suppression changed | suppressed={0} stage={1}")]
    public void SuppressionChanged(bool suppressed, string stage)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSuppressionChanged, suppressed, stage);
    }

    [Event(EvtSystemSuspended,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "system suspended | reason={0}")]
    public void SystemSuspended(string reason)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSystemSuspended, reason);
    }

    [Event(EvtSystemResumed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "system resumed | reason={0}")]
    public void SystemResumed(string reason)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSystemResumed, reason);
    }

    // ── Setup failures ────────────────────────────────────────────────────────

    [Event(EvtCursorHookFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cursor WinEvent hook failed — the cover cannot react to the mouse")]
    public void CursorHookFailed()
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCursorHookFailed);
    }

    [Event(EvtSessionNotifyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "session change notification unavailable — cover will not pause on lock")]
    public void SessionNotifyFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSessionNotifyFailed);
    }
}
