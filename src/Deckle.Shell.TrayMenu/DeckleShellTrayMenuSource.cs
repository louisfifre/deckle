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
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info is a short
// Capital sentence with no IDs and no k=v; the technical detail (handles,
// durations, dismiss reason, item label) lives in a Verbose mirror that FOLLOWS
// it.
[EventSource(Name = "Deckle-TrayMenu")]
public sealed class DeckleShellTrayMenuSource : DeckleEventSource
{
    public static readonly DeckleShellTrayMenuSource Log = new();

    private DeckleShellTrayMenuSource() { }

    internal static bool IsDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Windowing, Log, level, keywords);

    // ── Event IDs ─────────────────────────────────────────────────────────────
    // Milestones keep their original id; the Verbose mirrors added for the
    // Verbose/Info separation take fresh ids 17-21 at the end of the sequence.
    // IDs are public in the ETW manifest; never reuse an id after deleting an event.
    public const int EvtHostConstructed           = 1;
    public const int EvtFlyoutBuilt               = 2;
    public const int EvtFrameLoaded               = 3;
    public const int EvtPrimeCycleStarted         = 4;
    public const int EvtPrimeCycleCompleted       = 5;
    public const int EvtShowRequested             = 6;
    public const int EvtAmbientStateRead          = 7;
    public const int EvtItemMeasured              = 8;
    public const int EvtFlyoutMeasured            = 9;
    public const int EvtFlyoutShownAt             = 10;
    public const int EvtHidden                    = 11;
    public const int EvtWindowActivated           = 12;
    public const int EvtFlyoutClosed              = 13;
    public const int EvtItemClicked               = 14;
    public const int EvtDisposed                  = 15;
    public const int EvtTaskbarCoverStateRead     = 16;
    public const int EvtHostConstructedDetail     = 17;
    public const int EvtPrimeCycleCompletedDetail = 18;
    public const int EvtShowRequestedDetail       = 19;
    public const int EvtHiddenDetail              = 20;
    public const int EvtItemClickedDetail         = 21;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Event(EvtHostConstructed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Host constructed")]
    public void HostConstructed()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHostConstructed);
    }

    [Event(EvtHostConstructedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "host constructed | owner_hwnd=0x{0:X}")]
    public void HostConstructedDetail(long owner_hwnd)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHostConstructedDetail, owner_hwnd);
    }

    [Event(EvtFlyoutBuilt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "menu flyout built | items={0}")]
    public void FlyoutBuilt(int items)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutBuilt, items);
    }

    [Event(EvtDisposed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Host disposed")]
    public void Disposed()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtDisposed);
    }

    // ── Prime cycle (Frame.Loaded one-shot) ──────────────────────────────────

    [Event(EvtFrameLoaded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Frame.Loaded fired | already_primed={0}")]
    public void FrameLoaded(bool already_primed)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFrameLoaded, already_primed);
    }

    [Event(EvtPrimeCycleStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "prime cycle started | style_before=0x{0:X} style_after=0x{1:X}")]
    public void PrimeCycleStarted(long style_before, long style_after)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPrimeCycleStarted, style_before, style_after);
    }

    [Event(EvtPrimeCycleCompleted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Visual tree primed")]
    public void PrimeCycleCompleted()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPrimeCycleCompleted);
    }

    [Event(EvtPrimeCycleCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "prime cycle completed | duration_ms={0:F2}")]
    public void PrimeCycleCompletedDetail(double duration_ms)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtPrimeCycleCompletedDetail, duration_ms);
    }

    // ── Show pipeline (per opening) ───────────────────────────────────────────

    [Event(EvtShowRequested,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Menu requested")]
    public void ShowRequested()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtShowRequested);
    }

    [Event(EvtShowRequestedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "show requested | ms_since_last_show={0:F1} | show_count={1}")]
    public void ShowRequestedDetail(double ms_since_last_show, int show_count)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtShowRequestedDetail, ms_since_last_show, show_count);
    }

    [Event(EvtAmbientStateRead,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ambient state read | is_on={0}")]
    public void AmbientStateRead(bool is_on)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtAmbientStateRead, is_on);
    }

    [Event(EvtTaskbarCoverStateRead,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "taskbar cover state read | is_on={0}")]
    public void TaskbarCoverStateRead(bool is_on)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtTaskbarCoverStateRead, is_on);
    }

    // ── Flyout measurement ───────────────────────────────────────────────────

    [Event(EvtItemMeasured,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item measured | idx={0} text=\"{1}\" type={2} desired_w={3:F1} desired_h={4:F1}")]
    public void ItemMeasured(int idx, string text, string type, double desired_w, double desired_h)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemMeasured, idx, text, type, desired_w, desired_h);
    }

    [Event(EvtFlyoutMeasured,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "flyout measured | dip_w={0:F1} dip_h={1:F1} physical={2}x{3} scale={4:F2}")]
    public void FlyoutMeasured(double dip_w, double dip_h, int physical_w, int physical_h, double scale)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutMeasured, dip_w, dip_h, physical_w, physical_h, scale);
    }

    // Constant placement (FlyoutPlacementMode.Full, FlyoutShowMode.Transient) is
    // documented at the ShowAt call site; the milestone carries no detail.
    [Event(EvtFlyoutShownAt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Menu shown")]
    public void FlyoutShownAt()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutShownAt);
    }

    // ── Dismiss & interactions ────────────────────────────────────────────────

    [Event(EvtHidden,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Menu hidden")]
    public void Hidden()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHidden);
    }

    [Event(EvtHiddenDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "hidden | reason={0}")]
    public void HiddenDetail(string reason)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtHiddenDetail, reason);
    }

    [Event(EvtWindowActivated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Window.Activated | state={0} is_visible={1}")]
    public void WindowActivated(string state, bool is_visible)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowActivated, state, is_visible);
    }

    [Event(EvtFlyoutClosed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Flyout.Closed | is_visible={0}")]
    public void FlyoutClosed(bool is_visible)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFlyoutClosed, is_visible);
    }

    [Event(EvtItemClicked,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A menu item was clicked")]
    public void ItemClicked()
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemClicked);
    }

    [Event(EvtItemClickedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item clicked | text=\"{0}\"")]
    public void ItemClickedDetail(string text)
    {
        if (IsDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemClickedDetail, text);
    }
}
