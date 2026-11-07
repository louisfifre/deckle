using Xunit;

namespace Deckle.Diagnostics.Logging.Tests;

// The follow-the-end rule of the LogWindow, on scroll observations alone:
// offset, scrollable height and extent, in DIPs.
[Trait("Category", "unit")]
public sealed class LogAutoScrollTests
{
    [Fact]
    public void ItFollowsTheEndByDefault()
    {
        Assert.True(new LogAutoScroll().IsFollowing);
    }

    [Fact]
    public void ScrollingUpPausesIt()
    {
        var scroll = new LogAutoScroll();
        scroll.Observe(offset: 1000, scrollableHeight: 1000, extentHeight: 1500);

        bool changed = scroll.Observe(offset: 900, scrollableHeight: 1000, extentHeight: 1500);

        Assert.True(changed);
        Assert.False(scroll.IsFollowing);
    }

    [Fact]
    public void ComingBackToTheBottomResumesIt()
    {
        var scroll = new LogAutoScroll();
        scroll.Observe(1000, 1000, 1500);
        scroll.Observe(900, 1000, 1500);

        bool changed = scroll.Observe(offset: 999, scrollableHeight: 1000, extentHeight: 1500);

        Assert.True(changed);
        Assert.True(scroll.IsFollowing);
    }

    [Fact]
    public void NewRowsBelowTheViewDoNotPauseIt()
    {
        // A row lands: the extent grows, the offset stays until the window
        // scrolls to the new end.
        var scroll = new LogAutoScroll();
        scroll.Observe(1000, 1000, 1500);

        Assert.False(scroll.Observe(offset: 1000, scrollableHeight: 1020, extentHeight: 1520));
        Assert.True(scroll.IsFollowing);
    }

    [Fact]
    public void TheRingDroppingItsOldestRowsDoesNotPauseIt()
    {
        // The oldest row leaves: the extent shrinks and the offset moves up
        // with it, which is not the reader scrolling.
        var scroll = new LogAutoScroll();
        scroll.Observe(1000, 1000, 1500);

        Assert.False(scroll.Observe(offset: 980, scrollableHeight: 1000, extentHeight: 1480));
        Assert.True(scroll.IsFollowing);
    }

    [Fact]
    public void APauseChosenWithTheToggleSurvivesReachingTheBottom()
    {
        var scroll = new LogAutoScroll();
        scroll.SetFollowing(false);
        scroll.Observe(500, 1000, 1500);

        Assert.False(scroll.Observe(offset: 1000, scrollableHeight: 1000, extentHeight: 1500));
        Assert.False(scroll.IsFollowing);
    }

    [Fact]
    public void TurningTheToggleBackOnFollowsAgain()
    {
        var scroll = new LogAutoScroll();
        scroll.Observe(1000, 1000, 1500);
        scroll.Observe(900, 1000, 1500);

        scroll.SetFollowing(true);

        Assert.True(scroll.IsFollowing);
    }

    [Fact]
    public void AStepShorterThanTheToleranceIsNotAScrollUp()
    {
        var scroll = new LogAutoScroll();
        scroll.Observe(1000, 1000, 1500);

        Assert.False(scroll.Observe(offset: 999, scrollableHeight: 1000, extentHeight: 1500));
        Assert.True(scroll.IsFollowing);
    }
}
