using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Shell.TrayMenu;

// Shell.TrayMenu module sub-provider: events specific to the WinUI 3 tray menu
// lifecycle and open pipeline. Strict window-positioning events (anchor,
// monitor, popup position, move-and-resize, foreground set) are emitted by the
// cross-cutting `DeckleWindowingSource` sub-provider through
// `WindowingProbe.Emit*` on the `TrayContextMenuHost` side, not duplicated
// here, in line with the `reference--eventsource-convention` doctrine that
// reserves cross-cutting positioning for the common Windowing trunk.
//
// What remains here is tray-menu-specific and has no equivalent in
// cross-cutting sub-providers: visual tree priming (prime cycle), individual
// flyout item measurement, dismiss qualification (deactivated / flyout_closed /
// item_click:<label>), item click. These traces diagnose flyout-specific bugs
// (zero measurement, excess height, unexpected dismiss) without drowning the
// signal in Windowing noise.
[EventSource(Name = "Deckle.Shell.TrayMenu")]
public sealed class DeckleShellTrayMenuSource : DeckleEventSource
{
    public static readonly DeckleShellTrayMenuSource Log = new();

    private DeckleShellTrayMenuSource() { }

    // ── Event IDs ─────────────────────────────────────────────────────────────
    public const int EvtHostConstructed       = 1;
    public const int EvtFlyoutBuilt           = 2;
    public const int EvtFrameLoaded           = 3;
    public const int EvtPrimeCycleStarted     = 4;
    public const int EvtPrimeCycleCompleted   = 5;
    public const int EvtShowRequested         = 6;
    public const int EvtAmbientStateRead      = 7;
    public const int EvtItemMeasured          = 8;
    public const int EvtFlyoutMeasured        = 9;
    public const int EvtFlyoutShownAt         = 10;
    public const int EvtHidden                = 11;
    public const int EvtWindowActivated       = 12;
    public const int EvtFlyoutClosed          = 13;
    public const int EvtItemClicked           = 14;
    public const int EvtDisposed              = 15;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Event(EvtHostConstructed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tray menu host constructed | owner_hwnd=0x{0:X}")]
    public void HostConstructed(long owner_hwnd)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHostConstructed, owner_hwnd);
    }

    [Event(EvtFlyoutBuilt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "menu flyout built | items={0}")]
    public void FlyoutBuilt(int items)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutBuilt, items);
    }

    [Event(EvtDisposed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tray menu host disposed")]
    public void Disposed()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDisposed);
    }

    // ── Prime cycle (Frame.Loaded one-shot) ──────────────────────────────────

    [Event(EvtFrameLoaded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Frame.Loaded fired | already_primed={0}")]
    public void FrameLoaded(bool already_primed)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFrameLoaded, already_primed);
    }

    [Event(EvtPrimeCycleStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "prime cycle started | style_before=0x{0:X} style_after=0x{1:X}")]
    public void PrimeCycleStarted(long style_before, long style_after)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPrimeCycleStarted, style_before, style_after);
    }

    [Event(EvtPrimeCycleCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "prime cycle completed | duration_ms={0:F2}")]
    public void PrimeCycleCompleted(double duration_ms)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPrimeCycleCompleted, duration_ms);
    }

    // ── Show pipeline (per opening) ───────────────────────────────────────────

    [Event(EvtShowRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Show() requested | ms_since_last_show={0:F1} show_count={1}")]
    public void ShowRequested(double ms_since_last_show, int show_count)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtShowRequested, ms_since_last_show, show_count);
    }

    [Event(EvtAmbientStateRead,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ambient state read | is_on={0}")]
    public void AmbientStateRead(bool is_on)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtAmbientStateRead, is_on);
    }

    // ── Flyout measurement ───────────────────────────────────────────────────

    [Event(EvtItemMeasured,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item measured | idx={0} text=\"{1}\" type={2} desired_w={3:F1} desired_h={4:F1}")]
    public void ItemMeasured(int idx, string text, string type, double desired_w, double desired_h)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemMeasured, idx, text, type, desired_w, desired_h);
    }

    [Event(EvtFlyoutMeasured,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "flyout measured | dip_w={0:F1} dip_h={1:F1} physical={2}x{3} scale={4:F2}")]
    public void FlyoutMeasured(double dip_w, double dip_h, int physical_w, int physical_h, double scale)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutMeasured, dip_w, dip_h, physical_w, physical_h, scale);
    }

    [Event(EvtFlyoutShownAt,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "MenuFlyout.ShowAt | placement=Full mode=Transient")]
    public void FlyoutShownAt()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutShownAt);
    }

    // ── Dismiss & interactions ────────────────────────────────────────────────

    [Event(EvtHidden,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tray menu hidden | reason={0}")]
    public void Hidden(string reason)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHidden, reason);
    }

    [Event(EvtWindowActivated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Window.Activated | state={0} is_visible={1}")]
    public void WindowActivated(string state, bool is_visible)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowActivated, state, is_visible);
    }

    [Event(EvtFlyoutClosed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Flyout.Closed | is_visible={0}")]
    public void FlyoutClosed(bool is_visible)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutClosed, is_visible);
    }

    [Event(EvtItemClicked,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item clicked | text=\"{0}\"")]
    public void ItemClicked(string text)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemClicked, text);
    }
}
