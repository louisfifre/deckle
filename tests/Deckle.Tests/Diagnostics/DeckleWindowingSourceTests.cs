using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Tests.Shared;
using Xunit;

namespace Deckle.Tests.Diagnostics;

// Sub-provider transverse — positionnement et dimensionnement de fenêtres.
// Trois events : WindowPositioned (tronc commun), OverlaySlotAssigned
// (spécialisé empilement), PopupAnchored (spécialisé ancrage parent). On
// fixe le contrat des trois — pos/size sérialisés en pixels écran absolus,
// hmon en long pour matcher GetWindowRect/MonitorFromWindow côté call site.
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
        Assert.Equal(true, ev.Payload?[15]);
        Assert.Equal(0, ev.Payload?[16]);
    }
}
