using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell.TaskbarCover;

// TaskbarCover module provider. Cursor movement itself is never logged, nor
// is each opacity step of the approach-zone fade: the WinEvent stream runs at
// input cadence and only visibility transitions carry diagnostic value;
// those surface through CoverShown / CoverHidden. Per-band events carry the
// band ordinal: 0 is the primary taskbar's band, the others follow the
// desktop top to bottom, left to right.
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
    public const int EvtHostStarted             = 1;
    public const int EvtHostStartedDetail       = 2;
    public const int EvtHostStartFailed         = 3;
    public const int EvtHostStartFailedDetail   = 4;
    public const int EvtHostStopped             = 5;
    public const int EvtHostStopTimedOut        = 6;
    public const int EvtLayoutRebuilt           = 7;
    public const int EvtLayoutRebuiltDetail     = 8;
    public const int EvtLayoutQueryFailed       = 9;
    public const int EvtCoverShown              = 10;
    public const int EvtCoverHidden             = 11;
    public const int EvtCoverSuppressed         = 12;
    public const int EvtCoverSuppressedDetail   = 13;
    public const int EvtCoverUnsuppressed       = 14;
    public const int EvtSystemSuspended         = 15;
    public const int EvtSystemSuspendedDetail   = 16;
    public const int EvtSystemResumed           = 17;
    public const int EvtSystemResumedDetail     = 18;
    public const int EvtCursorHookFailed        = 19;
    public const int EvtSessionNotifyFailed     = 20;
    // 21, 22, 23 and 25 belong to retired events: reserved, never reuse.
    public const int EvtLayoutQueryRecovered    = 24;
    public const int EvtCoverUnsuppressedDetail = 26;
    public const int EvtBandCreateFailed        = 27;
    public const int EvtBandCreateFailedDetail  = 28;
    public const int EvtCoverRestacked          = 29;
    public const int EvtReorderHookFailed       = 30;

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

    // One line per band the rebuild placed; taskbars is the count the layout
    // found, so a reader sees at once whether every monitor got its band,
    // and scale is the monitor's DPI scale in percent (150 at 144 DPI), the
    // figure the band's approach span was multiplied by, so a reader can
    // check from the log alone that a span followed the display scale.
    [Event(EvtLayoutRebuiltDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "layout | band={0} | taskbars={1} | edge={2} | rect=({3},{4})-({5},{6}) | scale={7}% | reason={8}")]
    public void LayoutRebuiltDetail(int band, int taskbars, string edge, int left, int top, int right, int bottom, int scalePercent, string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtLayoutRebuiltDetail, band, taskbars, edge, left, top, right, bottom, scalePercent, reason);
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

    [Event(EvtBandCreateFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "A band window could not be created — that taskbar stays uncovered")]
    public void BandCreateFailed()
    {
        if (IsEnabled()) WriteEvent(EvtBandCreateFailed);
    }

    [Event(EvtBandCreateFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "band create failed | band={0} | win32_error={1}")]
    public void BandCreateFailedDetail(int band, int win32_error)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtBandCreateFailedDetail, band, win32_error);
    }

    // ── Cover visibility ──────────────────────────────────────────────────────
    //
    // Verbose: these flip on every approach and retreat of the cursor, noise
    // in the LogWindow outside a state-machine diagnosis.

    [Event(EvtCoverShown,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cover shown | band={0} | reason={1}")]
    public void CoverShown(int band, string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverShown, band, reason);
    }

    [Event(EvtCoverHidden,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "cover hidden | band={0} | reason={1}")]
    public void CoverHidden(int band, string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverHidden, band, reason);
    }

    // A band found its taskbar above it and climbed back. Verbose and gated
    // like the other cover transitions: it fires at the cadence of Explorer's
    // restacks, a diagnosis signal rather than a milestone.
    [Event(EvtCoverRestacked,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "cover restacked above its taskbar | band={0} | reason={1}")]
    public void CoverRestacked(int band, string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtCoverRestacked, band, reason);
    }

    // ── Gates ─────────────────────────────────────────────────────────────────

    [Event(EvtCoverSuppressed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A fullscreen app is foreground — the cover on its screen stands down")]
    public void CoverSuppressed()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCoverSuppressed);
    }

    [Event(EvtCoverSuppressedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "suppressed | band={0} | stage={1}")]
    public void CoverSuppressedDetail(int band, string stage)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverSuppressedDetail, band, stage);
    }

    [Event(EvtCoverUnsuppressed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The fullscreen app is gone — the cover on its screen is back")]
    public void CoverUnsuppressed()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCoverUnsuppressed);
    }

    [Event(EvtCoverUnsuppressedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "unsuppressed | band={0}")]
    public void CoverUnsuppressedDetail(int band)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverUnsuppressedDetail, band);
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

    [Event(EvtReorderHookFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The desktop reorder hook failed — a taskbar restacked above the cover is only caught later")]
    public void ReorderHookFailed()
    {
        if (IsEnabled()) WriteEvent(EvtReorderHookFailed);
    }

    [Event(EvtSessionNotifyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Session change notifications unavailable — the cover will not pause on lock")]
    public void SessionNotifyFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSessionNotifyFailed);
    }
}
