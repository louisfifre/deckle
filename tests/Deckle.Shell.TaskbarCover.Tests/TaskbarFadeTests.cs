using Deckle.Core;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (Louis, 2026-11-26): the taskbar itself fades away from
// the cursor and comes back whole as the cursor reaches it, and it never fades
// so far that a click on it goes through to the desktop.
[Trait("Category", "unit")]
public class TaskbarFadeTests
{
    [Fact]
    public void ACursorOnTheTaskbarLeavesItWhole()
    {
        Assert.Equal(TaskbarFade.Opaque, TaskbarFade.AlphaFor(0));
    }

    [Fact]
    public void ACursorFarAwayFadesTheTaskbarToTheFloor()
    {
        Assert.Equal(TaskbarFade.Floor, TaskbarFade.AlphaFor(255));
    }

    [Fact]
    public void TheTaskbarNeverFadesBelowTheFloor()
    {
        Assert.True(TaskbarFade.Floor > 0);
        for (int level = 0; level <= 255; level++)
            Assert.True(TaskbarFade.AlphaFor((byte)level) >= TaskbarFade.Floor);
    }

    [Fact]
    public void TheTaskbarFadesFurtherAsTheLevelRises()
    {
        for (int level = 1; level <= 255; level++)
            Assert.True(TaskbarFade.AlphaFor((byte)level) <= TaskbarFade.AlphaFor((byte)(level - 1)));
    }

    // Explorer re-creates its taskbar on a restart at the same rect. The new
    // window has never been faded, so the band must read it as a new placement.
    [Fact]
    public void ARecreatedTaskbarAtTheSameRectIsANewPlacement()
    {
        var rect = new NativeMethods.RECT { left = 0, top = 0, right = 48, bottom = 1440 };
        var monitor = new NativeMethods.RECT { left = 0, top = 0, right = 2560, bottom = 1440 };
        var before = new TaskbarPlacement((IntPtr)0x1000, rect, TaskbarEdge.Left, IntPtr.Zero, 1.0) { MonitorRect = monitor };
        var after = before with { Taskbar = (IntPtr)0x2000 };

        var band = new CoverBand(0);
        band.Place(before, ApproachDistance.DefaultDip);

        Assert.False(band.PlacementDiffers(before, ApproachDistance.DefaultDip));
        Assert.True(band.PlacementDiffers(after, ApproachDistance.DefaultDip));
    }
}
