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
    // 3 retired.
    // 4 retired.
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
    // 19 retired.
    // 20 retired.
    // 21, 22, 23 and 25 belong to retired events: reserved, never reuse.
    public const int EvtLayoutQueryRecovered    = 24;
    public const int EvtCoverUnsuppressedDetail = 26;
    // 27 retired.
    public const int EvtBandCreateFailedDetail  = 28;
    public const int EvtCoverRestacked          = 29;
    // 30 retired.
    public const int EvtHostStartFailed         = 31;
    public const int EvtReorderHookFailed       = 32;
    public const int EvtSessionNotifyFailed     = 33;
    public const int EvtBandCreateFailed        = 34;
    public const int EvtBandCreateRecovered     = 35;
    public const int EvtSettingsLoadWarning = 36;
    public const int EvtSettingsLoadError = 37;

    // ── Host lifecycle ────────────────────────────────────────────────────────

    [Event(EvtHostStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Host started")]
    public void HostStarted()
    {
        if (IsEnabled()) WriteEvent(EvtHostStarted);
    }

    [Event(EvtHostStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hwnd=0x{0:X} | thread_id={1}")]
    public void HostStartedDetail(long hwnd, int thread_id)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtHostStartedDetail, hwnd, thread_id);
    }

    // reason: previous_worker_alive (a stuck cover thread still owns the
    // native state; ex_type and ex_message stay empty) or setup_failed (the
    // native setup threw; the Win32 code, when there is one, is in ex_message).
    [Event(EvtHostStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The taskbar cover failed to start, so the taskbar stays uncovered\nreason={0} | ex_type={1} | ex_message={2}")]
    public void HostStartFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtHostStartFailed, reason, ex_type, ex_message);
    }

    [Event(EvtHostStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Host stopped")]
    public void HostStopped()
    {
        if (IsEnabled()) WriteEvent(EvtHostStopped);
    }

    [Event(EvtHostStopTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The taskbar cover thread did not stop in time, so the cover cannot restart until it does")]
    public void HostStopTimedOut()
    {
        if (IsEnabled()) WriteEvent(EvtHostStopTimedOut);
    }

    // ── Taskbar layout ────────────────────────────────────────────────────────

    [Event(EvtLayoutRebuilt,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "The taskbar cover layout was rebuilt")]
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
           Message = "band={0} | taskbars={1} | edge={2} | rect=({3},{4})-({5},{6}) | scale={7}% | reason={8}")]
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
           Tags = ObservationTags.Milestone,
           Message = "Taskbar location recovered")]
    public void LayoutQueryRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtLayoutQueryRecovered);
    }

    // One Warning per incident: every rebuild retries the missing band, and
    // each raw attempt lands in BandCreateFailedDetail. band and win32_error
    // are the attempt that opened the incident.
    [Event(EvtBandCreateFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "A band window could not be created — that taskbar stays uncovered\nband={0} | win32_error={1}")]
    public void BandCreateFailed(int band, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtBandCreateFailed, band, win32_error);
    }

    [Event(EvtBandCreateRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Windowing,
           Tags = ObservationTags.Milestone,
           Message = "Every taskbar has its band again")]
    public void BandCreateRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtBandCreateRecovered);
    }

    [Event(EvtBandCreateFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "band={0} | win32_error={1}")]
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
           Message = "band={0} | reason={1}")]
    public void CoverShown(int band, string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverShown, band, reason);
    }

    [Event(EvtCoverHidden,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "band={0} | reason={1}")]
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
           Message = "band={0} | reason={1}")]
    public void CoverRestacked(int band, string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtCoverRestacked, band, reason);
    }

    // ── Gates ─────────────────────────────────────────────────────────────────

    [Event(EvtCoverSuppressed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar cover stands down for a fullscreen app")]
    public void CoverSuppressed()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCoverSuppressed);
    }

    [Event(EvtCoverSuppressedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "band={0} | stage={1}")]
    public void CoverSuppressedDetail(int band, string stage)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverSuppressedDetail, band, stage);
    }

    [Event(EvtCoverUnsuppressed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar cover is back after the fullscreen app left")]
    public void CoverUnsuppressed()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCoverUnsuppressed);
    }

    [Event(EvtCoverUnsuppressedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "band={0}")]
    public void CoverUnsuppressedDetail(int band)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtCoverUnsuppressedDetail, band);
    }

    [Event(EvtSystemSuspended,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar cover is parked for sleep or lock")]
    public void SystemSuspended()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSystemSuspended);
    }

    [Event(EvtSystemSuspendedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0}")]
    public void SystemSuspendedDetail(string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtSystemSuspendedDetail, reason);
    }

    [Event(EvtSystemResumed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar cover resumed after sleep or unlock")]
    public void SystemResumed()
    {
        if (IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSystemResumed);
    }

    [Event(EvtSystemResumedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0}")]
    public void SystemResumedDetail(string reason)
    {
        if (!IsWindowingDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtSystemResumedDetail, reason);
    }

    // ── Setup and runtime failures ────────────────────────────────────────────

    // A cursor hook failure is not an event of its own: it fails the setup
    // and surfaces as HostStartFailed with reason=setup_failed.

    // SetWinEventHook documents no GetLastError, so win32_error may read 0.
    [Event(EvtReorderHookFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The desktop reorder hook failed — a taskbar restacked above the cover is only caught later\nwin32_error={0}")]
    public void ReorderHookFailed(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtReorderHookFailed, win32_error);
    }

    [Event(EvtSessionNotifyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Session change notifications unavailable — the cover will not pause on lock\nwin32_error={0}")]
    public void SessionNotifyFailed(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtSessionNotifyFailed, win32_error);
    }

    // The settings store reports in free text; the text carries its own cause.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar cover settings store worked around a problem\ndetail={0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The taskbar cover settings could not be saved or applied\ndetail={0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }
}
