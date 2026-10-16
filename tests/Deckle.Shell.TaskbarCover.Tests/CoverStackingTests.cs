using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// Window handles are opaque, so any distinct values stand for distinct
// windows. A chain maps each window to the one above it; the top maps to
// zero, as GetWindow(GW_HWNDPREV) does.
public class CoverStackingTests
{
    private static readonly IntPtr Band    = new(0x1001);
    private static readonly IntPtr Taskbar = new(0x1002);
    private static readonly IntPtr Other   = new(0x1003);

    private static Func<IntPtr, IntPtr> Chain(params IntPtr[] bottomToTop)
    {
        var above = new Dictionary<IntPtr, IntPtr>();
        for (int i = 0; i < bottomToTop.Length; i++)
            above[bottomToTop[i]] = i + 1 < bottomToTop.Length ? bottomToTop[i + 1] : IntPtr.Zero;
        return h => above.TryGetValue(h, out var p) ? p : IntPtr.Zero;
    }

    [Fact]
    [Trait("Category", "unit")]
    public void TaskbarDirectlyAboveTheBandIsAbove()
    {
        Assert.True(CoverStacking.TaskbarAbove(Band, Taskbar, Chain(Band, Taskbar)));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void TaskbarHigherUpTheStackIsAbove()
    {
        Assert.True(CoverStacking.TaskbarAbove(Band, Taskbar, Chain(Band, Other, Taskbar)));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void TaskbarBelowTheBandIsNotAbove()
    {
        Assert.False(CoverStacking.TaskbarAbove(Band, Taskbar, Chain(Taskbar, Band, Other)));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void BandAtTheTopHasNothingAbove()
    {
        Assert.False(CoverStacking.TaskbarAbove(Band, Taskbar, Chain(Taskbar, Other, Band)));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void MissingHandleIsNotAbove()
    {
        Assert.False(CoverStacking.TaskbarAbove(IntPtr.Zero, Taskbar, Chain(Band, Taskbar)));
        Assert.False(CoverStacking.TaskbarAbove(Band, IntPtr.Zero, Chain(Band, Taskbar)));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void CyclicChainStopsAtTheClimbBound()
    {
        int steps = 0;
        Func<IntPtr, IntPtr> cycle = _ => { steps++; return Other; };

        Assert.False(CoverStacking.TaskbarAbove(Band, Taskbar, cycle));
        Assert.Equal(CoverStacking.MaxClimb + 1, steps);
    }
}
