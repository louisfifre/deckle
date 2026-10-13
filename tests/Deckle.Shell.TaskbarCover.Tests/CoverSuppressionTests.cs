using Deckle.Shell.Foreground;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// Monitor handles are opaque, so any two distinct values stand for two
// monitors.
public class CoverSuppressionTests
{
    private static readonly IntPtr BandMonitor = new(0x10001);
    private static readonly IntPtr OtherMonitor = new(0x10002);

    private static ForegroundState Foreground(IntPtr monitor, FullscreenStage stage) =>
        new(new IntPtr(0x2000), 1234, "app", monitor, default, default, stage);

    [Fact]
    [Trait("Category", "regression")]
    public void FullscreenOnAnotherMonitorLeavesTheCoverUp()
    {
        var state = Foreground(OtherMonitor, FullscreenStage.Geometry);

        Assert.False(CoverSuppression.Applies(state, BandMonitor));
    }

    [Theory]
    [Trait("Category", "unit")]
    [InlineData(FullscreenStage.NotificationState)]
    [InlineData(FullscreenStage.Geometry)]
    public void FullscreenOnTheBandMonitorSuppressesTheCover(FullscreenStage stage)
    {
        var state = Foreground(BandMonitor, stage);

        Assert.True(CoverSuppression.Applies(state, BandMonitor));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void WindowedAppOnTheBandMonitorLeavesTheCoverUp()
    {
        var state = Foreground(BandMonitor, FullscreenStage.None);

        Assert.False(CoverSuppression.Applies(state, BandMonitor));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void NoKnownForegroundLeavesTheCoverUp()
    {
        Assert.False(CoverSuppression.Applies(ForegroundState.None, BandMonitor));
    }
}
