using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: positioning and sizing of every WinUI 3 or Win32
// app window (HUD, HudOverlay, tray popup, SettingsWindow, LogWindow,
// SetupWindow, FolderPicker). Without this event, a DPI or multi-monitor
// placement bug leaves no trace. The primitive is strictly non-business
// (platform wiring) and consumed by several modules with exactly the same
// parameter set.
//
// Coordinate convention: absolute screen pixels everywhere. Internal
// calculations may start from DIPs, but events always carry pixels to allow
// reversal through `dpi`. Consistent with what `GetCursorPos`, `GetWindowRect`,
// and `GetMonitorInfo` return.
//
// `WindowPositioned` is the common trunk emitted by every site that positions
// or resizes a window. Stacked overlays ALSO emit `OverlaySlotAssigned`, popups
// anchored to a parent control ALSO emit `PopupAnchored`, and topmost
// no-activate surfaces may ALSO emit `WindowZOrderState`. `WindowResizeSettled`
// and its per-frame companion `WindowResizeFrame` stand alone: a resize
// coalescer, not a positioning site, raises them.
//
// Two tiers. The measurements above are governed by the Windowing activity:
// they cost P/Invokes and run per show or per resize frame. A failure is not a
// measurement: `WindowShowFailed`, `WindowPlacementFailed` and the incident
// recovery bypass the activity, so a HUD that never appeared
// leaves a trace on a default install. `WindowingProbe.Report*` runs the
// incident rule for the show and placement failures.
//
// Closed `window` vocabulary (short logical name for the common trunk):
//   "hud"           — main HudWindow (bottom-center)
//   "hud-overlay"   — stacked transient HudOverlayWindow card
//   "transcript-panel" — TranscriptPanelWindow, the live dictation transcript
//   "settings"      — SettingsWindow
//   "log"           — LogWindow
//   "setup"         — SetupWindow first-run wizard
//   "playground"    — PlaygroundWindow (Hud + Ambient tuning shell)
//   "tray-popup"    — tray icon context menu anchor (right click)
//   "tray-actions"  — tray icon quick-actions menu anchor (left click)
//   "folder-picker" — system FolderPicker opened from Settings
//   "taskbar-cover" — opaque band masking the taskbar (native, own thread)
// Any new window added to the project must extend this vocabulary before
// emission, to preserve listener-side grep-ability.
//
// Closed `anchor` vocabulary (code-side placement intent, not a measurement):
//   "BottomCenter"    — HUD in BottomCenter mode (default)
//   "TopCenter"       — HUD in TopCenter mode
//   "Center"          — window centered on the work area (Settings, Log,
//                       Setup)
//   "CursorRelative"  — placement relative to the cursor (tray popup)
//   "ParentRelative"  — placement relative to a parent control (folder
//                       picker)
//   "absolute"        — no logical anchor, only a move/resize (raw Win32
//                       placement)
[EventSource(Name = "Deckle-Windowing")]
public sealed class DeckleWindowingSource : DeckleEventSource
{
    public static readonly DeckleWindowingSource Log = new();

    private DeckleWindowingSource() { }

    private bool IsWindowingDetailEnabled()
        => OperationalLogAdmission.IsEnabled(OperationalLogActivity.Windowing)
        && IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing);

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtWindowPositioned     = 1;
    public const int EvtOverlaySlotAssigned  = 2;
    public const int EvtPopupAnchored        = 3;
    public const int EvtWindowZOrderState    = 4;
    public const int EvtWindowResizeSettled  = 5;
    public const int EvtWindowResizeFrame    = 6;
    public const int EvtWindowLoadComplete   = 7;
    public const int EvtWindowShowFailed     = 8;
    public const int EvtWindowPlacementFailed = 9;
    public const int EvtWindowFailureRepeated = 10;
    public const int EvtWindowFailureRecovered = 11;
    // 12 retired.

    // Common trunk. `hmon` comes from `MonitorFromWindow`, `dpi` from
    // `GetDpiForWindow`.
    [Event(EvtWindowPositioned,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "window positioned | window={0} | hmon=0x{1:X} | dpi={2} | anchor={3} | pos={4},{5} size={6},{7}")]
    public void WindowPositioned(
        string window, long hmon, int dpi, string anchor,
        int pos_x, int pos_y, int size_w, int size_h)
    {
        if (!IsWindowingDetailEnabled()) return;
        WriteEvent(EvtWindowPositioned, window, hmon, dpi, anchor, pos_x, pos_y, size_w, size_h);
    }

    // Stacked overlay specialization: `slot=0` is the closest to the main HUD.
    // `WindowPositioned` is also emitted with window="hud-overlay", so the
    // trunk stays complete while a listener can follow overlays alone.
    [Event(EvtOverlaySlotAssigned,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "overlay slot | slot={0} | hmon=0x{1:X} | pos={2},{3} size={4},{5}")]
    public void OverlaySlotAssigned(
        int slot, long hmon,
        int pos_x, int pos_y, int size_w, int size_h)
    {
        if (!IsWindowingDetailEnabled()) return;
        WriteEvent(EvtOverlaySlotAssigned, slot, hmon, pos_x, pos_y, size_w, size_h);
    }

    // Anchored popup specialization: `parent_rect` is the anchored control's
    // rectangle (e.g. tray icon, FolderPicker button) in absolute screen
    // pixels, serialized as string "x,y,w,h" to fit in 6 EventSource
    // parameters. A popup whose HWND the app does not own (system FolderPicker
    // dialog) emits only this event, without the common trunk.
    [Event(EvtPopupAnchored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "popup anchored | popup={0} | parent_rect={1} | pos={2},{3} size={4},{5}")]
    public void PopupAnchored(
        string popup, string parent_rect,
        int pos_x, int pos_y, int size_w, int size_h)
    {
        if (!IsWindowingDetailEnabled()) return;
        WriteEvent(EvtPopupAnchored, popup, parent_rect, pos_x, pos_y, size_w, size_h);
    }

    // Z-order specialization: captures native state around a ShowWindow /
    // SetWindowPos topmost operation. `visible_above_count` remains raw
    // z-order: a Win32-visible window can be cloaked, outside the target
    // rectangle, or a Shell/IME helper without real occlusion.
    // `occluding_above_count` is the real occlusion signal: visible,
    // non-cloaked, non-empty rect, and geometric intersection with the target.
    // `setpos_ok/last_error` reflect the SetWindowPos call when the stage
    // follows that call, otherwise true/0. They describe the z-order context;
    // the failure itself reaches the log through WindowShowFailed.
    [Event(EvtWindowZOrderState,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "z-order state | window={0} | stage={1} | visible={2} | topmost={3} | previous_visible={4} | previous_topmost={5} | foreground_pid={6} | foreground_class={7} | previous_hwnd=0x{8:X} | previous_pid={9} | previous_class={10} | visible_above_count={11} | first_visible_above_pid={12} | first_visible_above_class={13} | first_visible_above_topmost={14} | occluding_above_count={15} | first_occluding_above_pid={16} | first_occluding_above_class={17} | first_occluding_above_topmost={18} | setpos_ok={19} | last_error={20}")]
    public void WindowZOrderState(
        string window, string stage,
        bool visible, bool topmost,
        bool previous_visible, bool previous_topmost,
        long foreground_pid, string foreground_class,
        long previous_hwnd, long previous_pid, string previous_class,
        int visible_above_count,
        long first_visible_above_pid,
        string first_visible_above_class,
        bool first_visible_above_topmost,
        int occluding_above_count,
        long first_occluding_above_pid,
        string first_occluding_above_class,
        bool first_occluding_above_topmost,
        bool setpos_ok, int last_error)
    {
        if (!IsWindowingDetailEnabled()) return;
        WriteEvent(
            EvtWindowZOrderState,
            window, stage, visible, topmost,
            previous_visible, previous_topmost,
            foreground_pid, foreground_class,
            previous_hwnd, previous_pid, previous_class,
            visible_above_count,
            first_visible_above_pid,
            first_visible_above_class,
            first_visible_above_topmost,
            occluding_above_count,
            first_occluding_above_pid,
            first_occluding_above_class,
            first_occluding_above_topmost,
            setpos_ok, last_error);
    }

    // One rolled-up event per *settled* resize, emitted by ResizeCoalescer at
    // the boundary where the window does its single deferred recompute.
    // `trigger` is a closed 2-value vocabulary: "gesture" (a user drag
    // bracketed by WM_ENTERSIZEMOVE / WM_EXITSIZEMOVE, `frames` = the WM_SIZE
    // frames coalesced into the one recompute, `duration_ms` = the gesture
    // wall-clock) or "direct" (a WM_SIZE outside any gesture — maximize / snap /
    // programmatic SetWindowPos — with `frames`=1 and `duration_ms`=0).
    [Event(EvtWindowResizeSettled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "resize settled | window={0} | trigger={1} | frames={2} | duration_ms={3}")]
    public void WindowResizeSettled(string window, string trigger, int frames, long duration_ms)
    {
        if (!IsWindowingDetailEnabled()) return;
        WriteEvent(EvtWindowResizeSettled, window, trigger, frames, duration_ms);
    }

    // Per-frame companion to `WindowResizeSettled`, for each coalesced WM_SIZE
    // of a drag gesture. `since_prev_ms` is the cadence Windows drives the
    // modal resize loop at; `relayout_ms` is WinUI's synchronous layout pass
    // for this frame (it runs inside DefSubclassProc). A near-zero
    // `relayout_ms` under a tight `since_prev_ms` points the lag below the
    // framework, at composition/present, not at Deckle's draw.
    [Event(EvtWindowResizeFrame,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "resize frame | window={0} | frame={1} | size={2},{3} | since_prev_ms={4} | relayout_ms={5}")]
    public void WindowResizeFrame(
        string window, int frame, int size_w, int size_h, long since_prev_ms, long relayout_ms)
    {
        if (!IsWindowingDetailEnabled()) return;
        WriteEvent(EvtWindowResizeFrame, window, frame, size_w, size_h, since_prev_ms, relayout_ms);
    }

    // Lazy first-open construction cost of a secondary window. `load_ms` spans
    // `new <Window>()` plus the synchronous placement-restore and first-time
    // wiring inside the lazy guard, NOT the per-open Show/Activate path. One
    // complete event with no paired start: the span is synchronous and has no
    // intermediate phase to bracket.
    //
    // Not governed: it fires at most once per window per process and costs
    // one Stopwatch, so a slow first open stays readable in a field report.
    [Event(EvtWindowLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "window load complete | window={0} | load_ms={1}")]
    public void WindowLoadComplete(string window, long load_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtWindowLoadComplete, window, load_ms);
    }

    // ── Failures (never governed) ───────────────────────────────────────
    //
    // `stage` names the call that failed, snake_case ("setwindowpos_topmost",
    // "show_window", "move_and_resize"). A Win32 call carries `win32_error`
    // (GetLastError right after the call) with hr=0 and empty ex_*; a WinRT or
    // managed call carries the exception and its HResult with win32_error=0.
    // Emitted through WindowingProbe.ReportShow*/ReportPlacement*, which opens
    // one incident per (window, stage).
    [Event(EvtWindowShowFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "A window failed to show | window={0} | stage={1} | hr=0x{2:X8} | win32_error={3} | ex_type={4} | ex_message={5}")]
    public void WindowShowFailed(
        string window, string stage, int hr, int win32_error, string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtWindowShowFailed, window, stage, hr, win32_error, ex_type, ex_message);
    }

    [Event(EvtWindowPlacementFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "A window could not be placed | window={0} | stage={1} | hr=0x{2:X8} | win32_error={3} | ex_type={4} | ex_message={5}")]
    public void WindowPlacementFailed(
        string window, string stage, int hr, int win32_error, string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtWindowPlacementFailed, window, stage, hr, win32_error, ex_type, ex_message);
    }

    // Raw attempt inside an open incident: the Warning already said the
    // window is failing, so a repeat is detail and follows the activity gate.
    // `failures` counts the incident's failures including this one.
    [Event(EvtWindowFailureRepeated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "window failure repeated | window={0} | stage={1} | failures={2} | hr=0x{3:X8} | win32_error={4}")]
    public void WindowFailureRepeated(string window, string stage, int failures, int hr, int win32_error)
    {
        if (!IsWindowingDetailEnabled()) return;
        WriteEvent(EvtWindowFailureRepeated, window, stage, failures, hr, win32_error);
    }

    // Closes the incident at the first success of the same (window, stage).
    // Journal-tagged: the journal keeps the Warning, so it keeps its end too.
    [Event(EvtWindowFailureRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Windowing,
           Tags = ObservationTags.Journal,
           Message = "A window shows and places again | window={0} | stage={1} | failures={2}")]
    public void WindowFailureRecovered(string window, string stage, int failures)
    {
        if (!IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Windowing)) return;
        WriteEvent(EvtWindowFailureRecovered, window, stage, failures);
    }
}
