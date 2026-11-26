using Deckle.Core;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (Louis, 2026-11-26): the strip under the faded taskbar
// carries on the move of the app that reaches or leaves the screen's edge,
// and a new look fades in, its blur rising with its opacity.
[Trait("Category", "unit")]
public class UnderlayMotionTests
{
    private static readonly NativeMethods.RECT LeftBar = new() { left = 0, top = 0, right = 64, bottom = 2160 };

    [Fact]
    public void BesideALeftTaskbarTheStripGrowsFromTheAppTowardTheScreensEdge()
    {
        var part = UnderlayMotion.Part(LeftBar, TaskbarEdge.Left, 0.25);

        Assert.Equal((48, 0, 64, 2160), (part.left, part.top, part.right, part.bottom));
    }

    [Fact]
    public void BesideABottomTaskbarTheStripGrowsDownFromTheApp()
    {
        var bar = new NativeMethods.RECT { left = 0, top = 2096, right = 3840, bottom = 2160 };

        var part = UnderlayMotion.Part(bar, TaskbarEdge.Bottom, 0.5);

        Assert.Equal((0, 2096, 3840, 2128), (part.left, part.top, part.right, part.bottom));
    }

    [Fact]
    public void AFinishedSlideInCoversTheWholePlace()
    {
        double extent = UnderlayMotion.SlideExtent(0, slideIn: true, UnderlayMotion.SlideMs);

        Assert.Equal(1, extent);
        Assert.True(UnderlayMotion.SlideDone(UnderlayMotion.SlideMs));
    }

    [Fact]
    public void ASlideInMovesFastestAtItsStart()
    {
        long third = UnderlayMotion.SlideMs / 3;
        double first = UnderlayMotion.SlideExtent(0, true, third);
        double second = UnderlayMotion.SlideExtent(0, true, 2 * third) - first;

        Assert.True(first > second);
    }

    [Fact]
    public void ASlideOutTurnedRoundMidwayStartsFromWhereTheStripIs()
    {
        double halfway = UnderlayMotion.SlideExtent(1, slideIn: false, UnderlayMotion.SlideMs / 2);

        Assert.Equal(halfway, UnderlayMotion.SlideExtent(halfway, slideIn: true, 0));
        Assert.Equal(0, UnderlayMotion.SlideExtent(halfway, slideIn: false, UnderlayMotion.SlideMs));
    }

    [Fact]
    public void TheOldLookFadesFromOpaqueToGone()
    {
        Assert.Equal(255, UnderlayMotion.FadeAlpha(0));
        Assert.InRange(UnderlayMotion.FadeAlpha(UnderlayMotion.FadeMs / 2), 1, 254);
        Assert.Equal(0, UnderlayMotion.FadeAlpha(UnderlayMotion.FadeMs));
    }
}
