using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell.TaskbarCover;

// TaskbarCover module provider: host lifecycle, taskbar layout, cover
// visibility transitions and the suppression/suspension gates. Cursor
// movement itself is never logged — the WinEvent stream runs at input
// cadence and only its state transitions (zone enter / re-cover) carry
// diagnostic value; those surface through CoverShown / CoverHidden.
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info is a
// short Capital sentence with no IDs and no k=v; the technical detail
// (handles, geometry, reasons) lives in a Verbose mirror that FOLLOWS it.
[EventSource(Name = "Deckle-TaskbarCover")]
public sealed class DeckleShellTaskbarCoverSource : DeckleEventSource
{
    public static readonly DeckleShellTaskbarCoverSource Log = new();

    private DeckleShellTaskbarCoverSource() { }

    private bool IsWindowingDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Windowing, this, level, keywords);

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // Sequential from 1. IDs are public in the ETW manifest; do not reuse an
    // ID after deleting an event.
    public const int EvtHostStarted           = 1;
    public const int EvtHostStartedDetail     = 2;
    public const int EvtHostStartFailed       = 3;
    public const int EvtHostStartFailedDetail = 4;
    public const int EvtHostStopped           = 5;
    public const int EvtHostStopTimedOut      = 6;
    public const int EvtLayoutRebuilt         = 7;
    public const int EvtLayoutRebuiltDetail   = 8;
    public const int EvtLayoutQueryFailed     = 9;
    public const int EvtCoverShown            = 10;
    public const int EvtCoverHidden           = 11;
    public const int EvtCoverSuppressed       = 12;
    public const int EvtCoverSuppressedDetail = 13;
    public const int EvtCoverUnsuppressed     = 14;
    public const int EvtSystemSuspended       = 15;
    public const int EvtSystemSuspendedDetail = 16;
    public const int EvtSystemResumed         = 17;
    public const int EvtSystemResumedDetail   = 18;
    public const int EvtCursorHookFailed      = 19;
    public const int EvtSessionNotifyFailed   = 20;
    public const int EvtTimerArmFailed        = 21;
    public const int EvtTimerArmFailedDetail  = 22;
    public const int EvtForegroundHookFailed  = 23;
    public const int EvtLayoutQueryRecovered  = 24;
    public const int EvtTimerArmRecovered     = 25;

    // ── Host lifecycle ────────────────────────────────────────────────────────

    [Event(EvtHostStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Host started")]
    public void HostStarted()
    {
        if (IsEnabled()) WriteEvent(EvtHostStarted);
    }

    [Event(EvtHostStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "host started | hwnd=0x{0:X} | thread_id={1}")]
    public void HostStartedDetail(long hwnd, int thread_id)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtHostStartedDetail, hwnd, thread_id);
    }

    [Event(EvtHostStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The host failed to start — the taskbar stays uncovered")]
    public void HostStartFailed()
    {
        if (IsEnabled()) WriteEvent(EvtHostStartFailed);
    }

    [Event(EvtHostStartFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "start failed | error_type={0} | message={1}")]
    public void HostStartFailedDetail(string error_type, string message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtHostStartFailedDetail, error_type, message);
    }

    [Event(EvtHostStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Host stopped")]
    public void HostStopped()
    {
        if (IsEnabled()) WriteEvent(EvtHostStopped);
    }

    [Event(EvtHostStopTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The cover thread did not exit in time — restart refused until it does")]
    public void HostStopTimedOut()
    {
        if (IsEnabled()) WriteEvent(EvtHostStopTimedOut);
    }

    // ── Taskbar layout ────────────────────────────────────────────────────────

    [Event(EvtLayoutRebuilt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "Layout rebuilt")]
    public void LayoutRebuilt()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing))
            WriteEvent(EvtLayoutRebuilt);
    }

    [Event(EvtLayoutRebuiltDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "layout | edge={0} | band=({1},{2})-({3},{4}) | reason={5}")]
    public void LayoutRebuiltDetail(string edge, int left, int top, int right, int bottom, string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtLayoutRebuiltDetail, edge, left, top, right, bottom, reason);
    }

    [Event(EvtLayoutQueryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "The taskbar could not be located — the cover stays hidden until it appears")]
    public void LayoutQueryFailed()
    {
        if (IsEnabled()) WriteEvent(EvtLayoutQueryFailed);
    }

    [Event(EvtLayoutQueryRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "Taskbar location recovered")]
    public void LayoutQueryRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtLayoutQueryRecovered);
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
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverShown, reason);
    }

    [Event(EvtCoverHidden,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cover hidden | reason={0}")]
    public void CoverHidden(string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverHidden, reason);
    }

    // ── Gates ─────────────────────────────────────────────────────────────────

    [Event(EvtCoverSuppressed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A fullscreen app is foreground — the cover stands down")]
    public void CoverSuppressed()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCoverSuppressed);
    }

    [Event(EvtCoverSuppressedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "suppressed | stage={0}")]
    public void CoverSuppressedDetail(string stage)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverSuppressedDetail, stage);
    }

    [Event(EvtCoverUnsuppressed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The fullscreen app is gone — the cover is back")]
    public void CoverUnsuppressed()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCoverUnsuppressed);
    }

    [Event(EvtSystemSuspended,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Parked for sleep or session lock")]
    public void SystemSuspended()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSystemSuspended);
    }

    [Event(EvtSystemSuspendedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "suspended | reason={0}")]
    public void SystemSuspendedDetail(string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtSystemSuspendedDetail, reason);
    }

    [Event(EvtSystemResumed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Resumed from sleep or unlock")]
    public void SystemResumed()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSystemResumed);
    }

    [Event(EvtSystemResumedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "resumed | reason={0}")]
    public void SystemResumedDetail(string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtSystemResumedDetail, reason);
    }

    // ── Setup and runtime failures ────────────────────────────────────────────

    [Event(EvtCursorHookFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The cursor hook failed — the cover cannot react to the mouse")]
    public void CursorHookFailed()
    {
        if (IsEnabled()) WriteEvent(EvtCursorHookFailed);
    }

    [Event(EvtSessionNotifyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Session change notifications unavailable — the cover will not pause on lock")]
    public void SessionNotifyFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSessionNotifyFailed);
    }

    [Event(EvtForegroundHookFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The foreground hook is unavailable — fullscreen and z-order recovery no longer follow app switches")]
    public void ForegroundHookFailed()
    {
        if (IsEnabled()) WriteEvent(EvtForegroundHookFailed);
    }

    [Event(EvtTimerArmFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The re-cover timer failed to arm — the band may not re-cover after the cursor leaves")]
    public void TimerArmFailed()
    {
        if (IsEnabled()) WriteEvent(EvtTimerArmFailed);
    }

    [Event(EvtTimerArmFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "timer arm failed | timer={0} | win32_error={1}")]
    public void TimerArmFailedDetail(string timer, int win32_error)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtTimerArmFailedDetail, timer, win32_error);
    }

    [Event(EvtTimerArmRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar re-cover timer recovered")]
    public void TimerArmRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtTimerArmRecovered);
    }
}
