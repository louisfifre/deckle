using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell.TrayMenu;

// Shell.TrayMenu module sub-provider: events specific to the tray menus'
// lifecycle and open pipeline. Strict window-positioning events (anchor rect,
// move-and-resize, foreground set) are emitted by the cross-cutting
// `DeckleWindowingSource` sub-provider through `WindowingProbe.Emit*` from the
// anchor window, not duplicated here, in line with the
// `reference--eventsource-convention` doctrine that reserves cross-cutting
// positioning for the common Windowing trunk.
//
// What remains here is tray-menu-specific: the side the menu opens on, dismiss
// qualification (deactivated / flyout_closed / icon_click / item_click:<id>),
// item click, the reopen guard, the anchor's activation.
//
// Verbose/Info separation per the deckle-logging skill: an Info is a short
// Capital sentence with no IDs and no k=v; the technical detail (handles,
// durations, dismiss reason, item id) lives in a Verbose mirror that FOLLOWS it.
[EventSource(Name = "Deckle-TrayMenu")]
public sealed class DeckleShellTrayMenuSource : DeckleEventSource
{
    public static readonly DeckleShellTrayMenuSource Log = new();

    private DeckleShellTrayMenuSource() { }

    internal static bool IsDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Windowing, Log, level, keywords);

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest; never reuse an id after deleting an
    // event. Retired with the measured carrier and the quick-actions panel
    // (2026-10-13): 5, 7, 8, 9, 16, 18, 19, 22-25, 28-35, 37-45.
    public const int EvtHostConstructed              = 1;
    public const int EvtFlyoutBuilt                  = 2;
    public const int EvtFrameLoaded                  = 3;
    public const int EvtCarrierStyleFinalized        = 4;
    public const int EvtShowRequested                = 6;
    public const int EvtFlyoutShownAt                = 10;
    public const int EvtHidden                       = 11;
    public const int EvtWindowActivated              = 12;
    public const int EvtFlyoutClosed                 = 13;
    public const int EvtItemClicked                  = 14;
    public const int EvtDisposed                     = 15;
    public const int EvtHostConstructedDetail        = 17;
    public const int EvtHiddenDetail                 = 20;
    public const int EvtItemClickedDetail            = 21;
    public const int EvtShowIgnoredAfterDismiss       = 26;
    public const int EvtShowIgnoredAfterDismissDetail = 27;
    public const int EvtActivationMovedToOwnedPopup  = 36;
    public const int EvtFlyoutShownAtDetail          = 46;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Event(EvtHostConstructed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray menu host was created")]
    public void HostConstructed()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHostConstructed);
    }

    [Event(EvtHostConstructedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "owner_hwnd=0x{0:X}")]
    public void HostConstructedDetail(long owner_hwnd)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHostConstructedDetail, owner_hwnd);
    }

    [Event(EvtFlyoutBuilt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "items={0}")]
    public void FlyoutBuilt(int items)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutBuilt, items);
    }

    [Event(EvtDisposed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray menu host was released")]
    public void Disposed()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDisposed);
    }

    // ── Anchor window ─────────────────────────────────────────────────────────

    [Event(EvtFrameLoaded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "already_loaded={0}")]
    public void FrameLoaded(bool already_loaded)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFrameLoaded, already_loaded);
    }

    [Event(EvtCarrierStyleFinalized,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "style_before=0x{0:X} | style_after=0x{1:X}")]
    public void CarrierStyleFinalized(long style_before, long style_after)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtCarrierStyleFinalized, style_before, style_after);
    }

    [Event(EvtWindowActivated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "state={0} | is_visible={1}")]
    public void WindowActivated(string state, bool is_visible)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowActivated, state, is_visible);
    }

    // The anchor lost activation to a window it owns (the menu's popup took
    // it): not a dismiss, the menu stays open.
    [Event(EvtActivationMovedToOwnedPopup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "target_hwnd=0x{0:X}")]
    public void ActivationMovedToOwnedPopup(long target_hwnd)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtActivationMovedToOwnedPopup, target_hwnd);
    }

    // ── Show pipeline (per opening) ───────────────────────────────────────────

    [Event(EvtShowRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray menu was requested")]
    public void ShowRequested()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtShowRequested);
    }

    [Event(EvtFlyoutShownAt,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray menu opened")]
    public void FlyoutShownAt()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutShownAt);
    }

    // window: "tray-popup", "tray-actions" (the Windowing vocabulary). side:
    // the TrayMenuSide chosen from the taskbar edge. anchor: the icon rect, or
    // the square around the cursor, in physical pixels.
    [Event(EvtFlyoutShownAtDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "window={0} | side={1} | anchor_left={2} | anchor_top={3} | anchor_right={4} | anchor_bottom={5}")]
    public void FlyoutShownAtDetail(
        string window, string side, int anchor_left, int anchor_top, int anchor_right, int anchor_bottom)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutShownAtDetail, window, side, anchor_left, anchor_top, anchor_right, anchor_bottom);
    }

    [Event(EvtShowIgnoredAfterDismiss,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray menu stays closed after the click that dismissed it")]
    public void ShowIgnoredAfterDismiss()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtShowIgnoredAfterDismiss);
    }

    [Event(EvtShowIgnoredAfterDismissDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ms_since_dismiss={0} | guard_ms={1}")]
    public void ShowIgnoredAfterDismissDetail(long ms_since_dismiss, long guard_ms)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtShowIgnoredAfterDismissDetail, ms_since_dismiss, guard_ms);
    }

    // ── Dismiss & interactions ────────────────────────────────────────────────

    [Event(EvtHidden,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The tray menu closed")]
    public void Hidden()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHidden);
    }

    // reason: "deactivated", "flyout_closed", "icon_click", "item_click:<id>",
    // "api".
    [Event(EvtHiddenDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0}")]
    public void HiddenDetail(string reason)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHiddenDetail, reason);
    }

    [Event(EvtFlyoutClosed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "is_visible={0}")]
    public void FlyoutClosed(bool is_visible)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutClosed, is_visible);
    }

    [Event(EvtItemClicked,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A tray menu item was clicked")]
    public void ItemClicked()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemClicked);
    }

    // id: the entry's stable id ("record", "ambient", "quit"...), never its label.
    [Event(EvtItemClickedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "id={0}")]
    public void ItemClickedDetail(string id)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemClickedDetail, id);
    }
}
