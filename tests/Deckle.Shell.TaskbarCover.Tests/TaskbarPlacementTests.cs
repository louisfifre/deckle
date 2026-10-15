using Deckle.Core;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (Louis, 2026-10-15): a taskbar's edge is read from its
// own rect against its own monitor, for the primary and every secondary
// alike, and the secondaries line up in desktop order so a band keeps its
// taskbar from one layout to the next.
[Trait("Category", "unit")]
public class TaskbarPlacementTests
{
    private static NativeMethods.RECT Rect(int left, int top, int right, int bottom) =>
        new() { left = left, top = top, right = right, bottom = bottom };

    // A 2560×1440 monitor at the desktop origin and a 44 or 48 px taskbar
    // on each of its edges, the same shapes CoverGeometryTests uses.
    private static readonly NativeMethods.RECT Monitor = Rect(0, 0, 2560, 1440);

    private static NativeMethods.RECT Taskbar(TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Left  => Rect(0, 0, 44, 1440),
        TaskbarEdge.Right => Rect(2516, 0, 2560, 1440),
        TaskbarEdge.Top   => Rect(0, 0, 2560, 48),
        _                 => Rect(0, 1392, 2560, 1440),
    };

    private static TaskbarPlacement At(int left, int top) =>
        new(IntPtr.Zero, Rect(left, top, left + 48, top + 48), TaskbarEdge.Left, IntPtr.Zero, 1.0);

    // ── The edge ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Top)]
    [InlineData(TaskbarEdge.Right)]
    [InlineData(TaskbarEdge.Bottom)]
    public void EdgeIsTheMonitorSideTheTaskbarHugs(TaskbarEdge edge)
    {
        Assert.Equal(edge, CoverGeometry.EdgeOf(Taskbar(edge), Monitor));
    }

    [Fact]
    public void EdgeOfASecondaryTaskbarIsReadAgainstItsOwnMonitor()
    {
        // The shape of Louis's desk: a 4K primary at 150 % with a 72 px
        // vertical taskbar on the left, and a smaller monitor below it that
        // shows its own taskbar, on its own left edge. Read against the
        // primary's monitor the secondary's rect would hug nothing; read
        // against its own it is a left taskbar like the primary's.
        var secondaryMonitor = Rect(226, 2160, 2206, 3480);
        var secondaryTaskbar = Rect(226, 2160, 298, 3480);

        Assert.Equal(TaskbarEdge.Left, CoverGeometry.EdgeOf(secondaryTaskbar, secondaryMonitor));
        Assert.Equal(TaskbarEdge.Bottom, CoverGeometry.EdgeOf(Rect(226, 3408, 2206, 3480), secondaryMonitor));
    }

    [Fact]
    public void EdgeOfAnAutoHiddenTaskbarIsTheSideItSlidOff()
    {
        // Auto-hide parks the window almost entirely past the monitor edge,
        // a sliver of it still on screen: the nearer side still names it.
        Assert.Equal(TaskbarEdge.Bottom, CoverGeometry.EdgeOf(Rect(0, 1438, 2560, 1486), Monitor));
        Assert.Equal(TaskbarEdge.Left,   CoverGeometry.EdgeOf(Rect(-42, 0, 2, 1440), Monitor));
        Assert.Equal(TaskbarEdge.Top,    CoverGeometry.EdgeOf(Rect(0, -46, 2560, 2), Monitor));
        Assert.Equal(TaskbarEdge.Right,  CoverGeometry.EdgeOf(Rect(2558, 0, 2602, 1440), Monitor));
    }

    [Fact]
    public void EdgeToleratesARectAPixelOffItsMonitor()
    {
        Assert.Equal(TaskbarEdge.Bottom, CoverGeometry.EdgeOf(Rect(0, 1391, 2560, 1439), Monitor));
        Assert.Equal(TaskbarEdge.Left,   CoverGeometry.EdgeOf(Rect(1, 0, 45, 1440), Monitor));
    }

    // ── Desktop order ────────────────────────────────────────────────────────

    [Fact]
    public void DesktopOrderRunsTopToBottomThenLeftToRight()
    {
        var above       = At(2560, 0);
        var belowLeft   = At(0, 2160);
        var belowRight  = At(1980, 2160);

        Assert.True(TaskbarPlacement.CompareDesktopOrder(above, belowLeft) < 0);
        Assert.True(TaskbarPlacement.CompareDesktopOrder(belowLeft, belowRight) < 0);
        Assert.True(TaskbarPlacement.CompareDesktopOrder(belowRight, above) > 0);
        Assert.Equal(0, TaskbarPlacement.CompareDesktopOrder(above, above));
    }

    [Fact]
    public void DesktopOrderIsTheSameWhateverTheOrderTheWindowsCameIn()
    {
        var expected = new[] { At(2560, 0), At(0, 2160), At(1980, 2160) };

        var walked = new List<TaskbarPlacement> { expected[2], expected[0], expected[1] };
        walked.Sort(TaskbarPlacement.CompareDesktopOrder);

        Assert.Equal(expected, walked);
    }
}
