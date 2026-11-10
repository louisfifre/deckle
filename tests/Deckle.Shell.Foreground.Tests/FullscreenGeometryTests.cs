using Deckle.Core;
using Deckle.Shell.Foreground;
using Xunit;

namespace Deckle.Shell.Foreground.Tests;

// Behaviour of FullscreenGeometry.CoversMonitor: a window reads as fullscreen
// when its visual bounds reach or pass every edge of its monitor, and only
// then — one pixel short on any side and it is an ordinary window.
[Trait("Category", "unit")]
public class FullscreenGeometryTests
{
    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom) =>
        new() { left = left, top = top, right = right, bottom = bottom };

    // A secondary monitor placed right of a 2560-wide primary, so no edge
    // sits at zero and a sign mistake cannot pass by accident.
    private static readonly NativeMethods.RECT Monitor = Rect(2560, 0, 4480, 1080);

    [Fact]
    public void Bounds_equal_to_the_monitor_cover_it()
    {
        Assert.True(FullscreenGeometry.CoversMonitor(Monitor, Monitor));
    }

    [Fact]
    public void Bounds_larger_on_every_side_cover_it()
    {
        var bounds = Rect(Monitor.left - 8, Monitor.top - 8, Monitor.right + 8, Monitor.bottom + 8);
        Assert.True(FullscreenGeometry.CoversMonitor(bounds, Monitor));
    }

    [Fact]
    public void Bounds_short_by_one_pixel_on_the_left_do_not_cover_it()
    {
        var bounds = Monitor with { left = Monitor.left + 1 };
        Assert.False(FullscreenGeometry.CoversMonitor(bounds, Monitor));
    }

    [Fact]
    public void Bounds_short_by_one_pixel_on_the_top_do_not_cover_it()
    {
        var bounds = Monitor with { top = Monitor.top + 1 };
        Assert.False(FullscreenGeometry.CoversMonitor(bounds, Monitor));
    }

    [Fact]
    public void Bounds_short_by_one_pixel_on_the_right_do_not_cover_it()
    {
        var bounds = Monitor with { right = Monitor.right - 1 };
        Assert.False(FullscreenGeometry.CoversMonitor(bounds, Monitor));
    }

    [Fact]
    public void Bounds_short_by_one_pixel_on_the_bottom_do_not_cover_it()
    {
        var bounds = Monitor with { bottom = Monitor.bottom - 1 };
        Assert.False(FullscreenGeometry.CoversMonitor(bounds, Monitor));
    }

    [Fact]
    public void Empty_bounds_do_not_cover_it()
    {
        Assert.False(FullscreenGeometry.CoversMonitor(default, Monitor));
    }

    [Fact]
    public void A_drawn_window_covering_the_monitor_reads_as_fullscreen()
    {
        Assert.True(FullscreenGeometry.ReadsAsFullscreen(drawn: true, Monitor, Monitor));
    }

    // The Alt+Tab switcher: a transparent Explorer window the size of the
    // monitor, already hidden when its foreground event is delivered. Read
    // as fullscreen, it held the taskbar cover down after the switch.
    [Fact]
    [Trait("Category", "regression")]
    public void A_hidden_window_covering_the_monitor_never_reads_as_fullscreen()
    {
        Assert.False(FullscreenGeometry.ReadsAsFullscreen(drawn: false, Monitor, Monitor));
    }

    [Fact]
    public void A_drawn_window_short_of_the_monitor_does_not_read_as_fullscreen()
    {
        var bounds = Monitor with { right = Monitor.right - 1 };
        Assert.False(FullscreenGeometry.ReadsAsFullscreen(drawn: true, bounds, Monitor));
    }
}
