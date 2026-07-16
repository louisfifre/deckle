using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: positioning and sizing of every WinUI 3 or Win32
// app window (HUD, HudOverlay, tray popup, SettingsWindow, LogWindow,
// SetupWindow, FolderPicker). Without this cross-cutting event, a DPI or
// multi-monitor placement bug leaves no trace; instrumentation would be done
// manually with `File.AppendAllText` at the exact site, the parallel path the
// centralization doctrine wants to avoid. The primitive is strictly
// non-business (platform wiring) and consumed by several modules with exactly
// the same parameter set: promotion to cross-cutting sub-provider under the
// two-clause criterion in `reference--eventsource-convention--1.2.md`
// §*Cross-cutting sub-providers*.
//
// Coordinate convention: absolute screen pixels everywhere. Internal
// calculations may start from DIPs, but events always carry pixels to allow
// reversal through `dpi`. Consistent with what `GetCursorPos`, `GetWindowRect`,
// and `GetMonitorInfo` return. See 1.2 §*Class 6 — Windowing* for the
// canonical parameter set.
//
// "Common trunk + specialized events" pattern. `WindowPositioned` is the trunk
// emitted by every site that positions or resizes a window. Stacked overlays
// ALSO emit `OverlaySlotAssigned` (the slot would not make sense on app
// windows). Popups anchored to a parent control ALSO emit `PopupAnchored` (with
// the anchored control rect serialized as string "x,y,w,h" to fit in 6
// EventSource parameters). Topmost/no-activate surfaces may ALSO emit
// `WindowZOrderState` to capture the native result of a z-order transition. A
// resizable window whose recompute is coalesced emits `WindowResizeSettled` once
// per settled resize — standalone, not a trunk specialization, since a coalescer
// rather than a positioning site raises it. Alongside it, `WindowResizeFrame`
// traces each coalesced WM_SIZE frame (size, cadence, WinUI relayout cost) for
// resize-latency diagnosis — deliberately chatty, Verbose-gated, off by default.
//
// Closed `window` vocabulary (short logical name for the common trunk):
//   "hud"           — main HudWindow (bottom-center)
//   "hud-overlay"   — stacked transient HudOverlayWindow card
//   "settings"      — SettingsWindow
//   "log"           — LogWindow
//   "setup"         — SetupWindow first-run wizard
//   "playground"    — PlaygroundWindow (Hud + Ambient tuning shell)
//   "tray-popup"    — tray icon context popup
//   "folder-picker" — system FolderPicker opened from Settings
//   "taskbar-cover" — opaque band masking the taskbar (native, own thread)
// Any new window added to the project must extend this vocabulary before
// emission, to preserve listener-side grep-ability.
//
// Closed `anchor` vocabulary (code-side placement intent, not a measurement):
//   "BottomCenter"    — HUD en mode BottomCenter (default)
//   "TopCenter"       — HUD en mode TopCenter
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

    // Common trunk: emitted by every site that positions or resizes a window.
    // `window` is a short logical name (see closed vocabulary above). `hmon`
    // is the monitor handle returned by `MonitorFromWindow`, `dpi` comes from
    // `GetDpiForWindow`, `anchor` describes the code-side chosen anchor, and
    // `pos`/`size` are in absolute screen pixels. Overlays and popups emit THIS
    // event in addition to their specialized event.
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

    // Stacked overlay specialization: `slot=0` for the closest to the main
    // HUD, `slot=1` for the next one, etc. `WindowPositioned` is also emitted
    // with window="hud-overlay" to preserve common trunk determinism and allow
    // a listener subscribing only to `OverlaySlotAssigned` to avoid noise from
    // non-overlay app windows.
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
    // parameters. `WindowPositioned` is also emitted with window="tray-popup"
    // or "folder-picker" for the common trunk when the popup is a window owned
    // by the app; popups whose HWND the app does not own (native
    // TrackPopupMenu menu, system FolderPicker dialog) only emit
    // `PopupAnchored` with what is known about the code-side trigger.
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
    // z-order: a Win32-visible window can be cloaked, outside the HUD
    // rectangle, or a Shell/IME helper without real occlusion.
    // `occluding_above_count` is the useful signal for the visual bug: visible,
    // non-cloaked, non-empty rect, and geometric intersection with the HUD.
    // `setpos_ok/last_error` reflect the SetWindowPos call when the stage
    // follows that call, otherwise true/0.
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

    // Resize-coalescing specialization: one rolled-up event per *settled* resize,
    // emitted by ResizeCoalescer at the boundary where the window does its single
    // deferred recompute — never one per WM_SIZE frame. `trigger` is a closed
    // 2-value vocabulary: "gesture" (a user drag bracketed by WM_ENTERSIZEMOVE /
    // WM_EXITSIZEMOVE, `frames` = the WM_SIZE frames coalesced into the one
    // recompute, `duration_ms` = the gesture wall-clock) or "direct" (a WM_SIZE
    // outside any gesture — maximize / snap / programmatic SetWindowPos — with
    // `frames`=1 and `duration_ms`=0, the safety net). `window` reuses the closed
    // logical-name vocabulary of the common trunk above.
    [Event(EvtWindowResizeSettled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "resize settled | window={0} | trigger={1} | frames={2} | duration_ms={3}")]
    public void WindowResizeSettled(string window, string trigger, int frames, long duration_ms)
    {
        if (!IsWindowingDetailEnabled()) return;
        WriteEvent(EvtWindowResizeSettled, window, trigger, frames, duration_ms);
    }

    // Per-frame companion to `WindowResizeSettled`, emitted by ResizeCoalescer for
    // each coalesced WM_SIZE while a drag gesture is in flight — the granular view
    // the rollup deliberately omits, for diagnosing resize lag. `frame` is the
    // 1-based index within the gesture, `size` the client area this frame in
    // pixels, `since_prev_ms` the wall time since the previous frame (the cadence
    // Windows drives the modal resize loop at), and `relayout_ms` the cost of
    // WinUI's synchronous layout pass for this frame (it runs inside
    // DefSubclassProc). A near-zero `relayout_ms` under a tight `since_prev_ms`
    // points the lag below the framework, at composition/present, not at our draw.
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

    // Lazy first-open construction cost: one rolled-up event per lazily
    // constructed secondary window (`window` reuses the closed logical-name
    // vocabulary above — "log", "settings", "playground"). `load_ms` is the
    // wall-clock of the one-shot construction span the call site brackets with a
    // Stopwatch: `new <Window>()` plus the synchronous placement-restore and
    // first-time wiring inside the lazy guard, NOT the per-open Show/Activate
    // path. Mirrors the whisper ModelLoadComplete(load_ms) shape; emitted as a
    // single complete event with no paired start, matching WindowResizeSettled —
    // the source's existing one-shot rolled-up-duration idiom — since the span
    // is synchronous, non-cancellable, and has no intermediate phase to bracket
    // externally.
    [Event(EvtWindowLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "window load complete | window={0} | load_ms={1}")]
    public void WindowLoadComplete(string window, long load_ms)
    {
        if (!IsWindowingDetailEnabled()) return;
        WriteEvent(EvtWindowLoadComplete, window, load_ms);
    }
}
