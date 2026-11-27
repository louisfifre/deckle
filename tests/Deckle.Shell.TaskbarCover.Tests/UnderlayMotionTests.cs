using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (Louis, 2026-11-26): every change of the strip under
// the faded taskbar dissolves, quickly, its blur rising and falling with its
// opacity; a slide was tried and dropped.
[Trait("Category", "unit")]
public class UnderlayMotionTests
{
    [Fact]
    public void AStripFadingInGoesFromNothingToOpaque()
    {
        Assert.Equal(0, UnderlayMotion.FadeAlpha(0, 255, 0));
        Assert.InRange(UnderlayMotion.FadeAlpha(0, 255, UnderlayMotion.FadeMs / 2), 1, 254);
        Assert.Equal(255, UnderlayMotion.FadeAlpha(0, 255, UnderlayMotion.FadeMs));
        Assert.True(UnderlayMotion.FadeDone(UnderlayMotion.FadeMs));
    }

    [Fact]
    public void AStripFadingOutGoesFromOpaqueToGone()
    {
        Assert.Equal(255, UnderlayMotion.FadeAlpha(255, 0, 0));
        Assert.Equal(0, UnderlayMotion.FadeAlpha(255, 0, UnderlayMotion.FadeMs));
    }

    [Fact]
    public void AFadeTurnedRoundMidwayStartsFromWhereTheStripStands()
    {
        byte halfway = UnderlayMotion.FadeAlpha(255, 0, UnderlayMotion.FadeMs / 2);

        Assert.Equal(halfway, UnderlayMotion.FadeAlpha(halfway, 255, 0));
        Assert.Equal(255, UnderlayMotion.FadeAlpha(halfway, 255, UnderlayMotion.FadeMs));
    }

    [Fact]
    public void AFadeChangesFastestAtItsStart()
    {
        long third = UnderlayMotion.FadeMs / 3;
        int first = UnderlayMotion.FadeAlpha(0, 255, third);
        int second = UnderlayMotion.FadeAlpha(0, 255, 2 * third) - first;

        Assert.True(first > second);
    }
}
