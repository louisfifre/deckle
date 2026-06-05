using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Cross-cutting sub-provider: window positioning and sizing.
// Trois events : WindowPositioned (tronc commun), OverlaySlotAssigned
// (stacking specialization), PopupAnchored (parent anchoring specialization).
// Freeze the contract of all three: pos/size serialized in absolute screen
// pixels, hmon as long to match GetWindowRect/MonitorFromWindow on the call
// site side.
[Trait("Category", "observability")]
public class DeckleWindowingSourceTests
{
    [Fact]
    public void WindowPositionedEmitsVerboseOnWindowingKeyword()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Windowing");

        DeckleWindowingSource.Log.WindowPositioned(
            window: "settings", hmon: 0x12345L, dpi: 144, anchor: "Center",
            pos_x: 100, pos_y: 200, size_w: 960, size_h: 1440);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleWindowingSource.EvtWindowPositioned, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Windowing));
        Assert.Equal("settings", ev.Payload?[0]);
        Assert.Equal(0x12345L, ev.Payload?[1]);
        Assert.Equal(144, ev.Payload?[2]);
        Assert.Equal("Center", ev.Payload?[3]);
        Assert.Equal(100, ev.Payload?[4]);
        Assert.Equal(200, ev.Payload?[5]);
        Assert.Equal(960, ev.Payload?[6]);
        Assert.Equal(1440, ev.Payload?[7]);
    }

    [Fact]
    public void OverlaySlotAssignedEmitsVerboseOnWindowingKeyword()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Windowing");

        DeckleWindowingSource.Log.OverlaySlotAssigned(
            slot: 2, hmon: 0xABCL,
            pos_x: 50, pos_y: 75, size_w: 400, size_h: 80);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleWindowingSource.EvtOverlaySlotAssigned, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.Equal(2, ev.Payload?[0]);
    }

    [Fact]
    public void PopupAnchoredSerialisesParentRectAsCommaSeparatedString()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Windowing");

        DeckleWindowingSource.Log.PopupAnchored(
            popup: "folder-picker", parent_rect: "10,20,300,40",
            pos_x: 0, pos_y: 0, size_w: 0, size_h: 0);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleWindowingSource.EvtPopupAnchored, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.Equal("folder-picker", ev.Payload?[0]);
        Assert.Equal("10,20,300,40", ev.Payload?[1]);
    }

    [Fact]
    public void WindowZOrderStateEmitsVerboseOnWindowingKeyword()
    {
        using var listener = new TestEventListener("Deckle.Diagnostics.Windowing");

        DeckleWindowingSource.Log.WindowZOrderState(
            window: "hud", stage: "after_setwindowpos_topmost",
            visible: true, topmost: true,
            previous_visible: true, previous_topmost: false,
            foreground_pid: 1001, foreground_class: "ApplicationFrameWindow",
            previous_hwnd: 0x123456, previous_pid: 1002, previous_class: "Shell_TrayWnd",
            visible_above_count: 1,
            first_visible_above_pid: 1002,
            first_visible_above_class: "Shell_TrayWnd",
            first_visible_above_topmost: true,
            occluding_above_count: 1,
            first_occluding_above_pid: 1003,
            first_occluding_above_class: "ApplicationFrameWindow",
            first_occluding_above_topmost: true,
            setpos_ok: true, last_error: 0);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleWindowingSource.EvtWindowZOrderState, ev.EventId);
        Assert.Equal(EventLevel.Verbose, ev.Level);
        Assert.True(ev.HasKeyword(Keywords.Windowing));
        Assert.Equal("hud", ev.Payload?[0]);
        Assert.Equal("after_setwindowpos_topmost", ev.Payload?[1]);
        Assert.Equal(true, ev.Payload?[2]);
        Assert.Equal(true, ev.Payload?[3]);
        Assert.Equal(true, ev.Payload?[4]);
        Assert.Equal(false, ev.Payload?[5]);
        Assert.Equal(1001L, ev.Payload?[6]);
        Assert.Equal("ApplicationFrameWindow", ev.Payload?[7]);
        Assert.Equal(0x123456L, ev.Payload?[8]);
        Assert.Equal(1002L, ev.Payload?[9]);
        Assert.Equal("Shell_TrayWnd", ev.Payload?[10]);
        Assert.Equal(1, ev.Payload?[11]);
        Assert.Equal(1002L, ev.Payload?[12]);
        Assert.Equal("Shell_TrayWnd", ev.Payload?[13]);
        Assert.Equal(true, ev.Payload?[14]);
        Assert.Equal(1, ev.Payload?[15]);
        Assert.Equal(1003L, ev.Payload?[16]);
        Assert.Equal("ApplicationFrameWindow", ev.Payload?[17]);
        Assert.Equal(true, ev.Payload?[18]);
        Assert.Equal(true, ev.Payload?[19]);
        Assert.Equal(0, ev.Payload?[20]);
    }

    [Fact]
    public void ZOrderSummarySeparatesRawVisibleWindowsFromActualOcclusion()
    {
        var hudRect = new WindowingProbe.WindowRect(Left: 100, Top: 100, Right: 372, Bottom: 178);
        var windowsAbove = new[]
        {
            new WindowingProbe.ZOrderWindow(
                Hwnd: 0x10, Pid: 2001, ClassName: "Shell_TrayWnd",
                Visible: true, Topmost: true, Cloaked: false,
                Rect: new WindowingProbe.WindowRect(0, 1030, 1920, 1080)),
            new WindowingProbe.ZOrderWindow(
                Hwnd: 0x11, Pid: 2002, ClassName: "Windows.UI.Core.CoreWindow",
                Visible: true, Topmost: true, Cloaked: true,
                Rect: new WindowingProbe.WindowRect(90, 90, 390, 190)),
            new WindowingProbe.ZOrderWindow(
                Hwnd: 0x12, Pid: 2003, ClassName: "ApplicationFrameWindow",
                Visible: true, Topmost: true, Cloaked: false,
                Rect: new WindowingProbe.WindowRect(80, 80, 420, 220)),
        };

        var summary = WindowingProbe.SummarizeWindowsAboveForTest(hudRect, windowsAbove);

        Assert.Equal(3, summary.VisibleCount);
        Assert.Equal(2001L, summary.FirstVisiblePid);
        Assert.Equal("Shell_TrayWnd", summary.FirstVisibleClassName);
        Assert.Equal(1, summary.OccludingCount);
        Assert.Equal(2003L, summary.FirstOccludingPid);
        Assert.Equal("ApplicationFrameWindow", summary.FirstOccludingClassName);
    }
}
