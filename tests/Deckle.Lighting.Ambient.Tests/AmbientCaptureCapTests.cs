using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The capture rate is min(display refresh, tier cap). The expectations read
// the tier constants for the cap and spell the display rates by hand, so a
// change of tier is one edit in AmbientCaptureCap and the cases still mean
// "this display, this mode".
[Trait("Category", "unit")]
public sealed class AmbientCaptureCapTests
{
    public static TheoryData<int, int> NormalMode() => new()
    {
        // display, expected target
        { 60, 60 },                              // slower than the cap: followed
        { 120, 120 },                            // at the cap
        { 144, AmbientCaptureCap.NormalCapHz },  // faster: held to the cap
        { 240, AmbientCaptureCap.NormalCapHz },
        { 480, AmbientCaptureCap.NormalCapHz },
        { 700, AmbientCaptureCap.NormalCapHz },
    };

    public static TheoryData<int, int> PerformanceMode() => new()
    {
        { 60, 60 },
        { 120, AmbientCaptureCap.PerformanceCapHz },
        { 144, AmbientCaptureCap.PerformanceCapHz },
        { 240, AmbientCaptureCap.PerformanceCapHz },
        { 480, AmbientCaptureCap.PerformanceCapHz },
        { 700, AmbientCaptureCap.PerformanceCapHz },
    };

    [Theory]
    [MemberData(nameof(NormalMode))]
    public void NormalModeFollowsTheDisplayUpToTheNormalCap(int displayHz, int expectedHz)
        => Assert.Equal(expectedHz, AmbientCaptureCap.TargetHz(displayHz, performanceMode: false));

    [Theory]
    [MemberData(nameof(PerformanceMode))]
    public void PerformanceModeFollowsTheDisplayUpToThePerformanceCap(int displayHz, int expectedHz)
        => Assert.Equal(expectedHz, AmbientCaptureCap.TargetHz(displayHz, performanceMode: true));

    [Fact]
    public void ThePerformanceCapIsBelowTheNormalCap()
        => Assert.True(AmbientCaptureCap.PerformanceCapHz < AmbientCaptureCap.NormalCapHz);

    [Theory]
    [InlineData(false, AmbientCaptureCap.NormalCapHz)]
    [InlineData(true, AmbientCaptureCap.PerformanceCapHz)]
    public void CapHzFollowsThePerformanceMode(bool performanceMode, int expectedCapHz)
        => Assert.Equal(expectedCapHz, AmbientCaptureCap.CapHz(performanceMode));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnUnknownDisplayRateFallsBackToTheSafeRateUnderTheCap(int displayHz)
    {
        Assert.Equal(AmbientCaptureCap.UnknownDisplayHz, AmbientCaptureCap.TargetHz(displayHz, performanceMode: false));
        Assert.Equal(AmbientCaptureCap.UnknownDisplayHz, AmbientCaptureCap.TargetHz(displayHz, performanceMode: true));
    }

    [Fact]
    public void TheTargetNeverExceedsTheCapOrTheKnownDisplay()
    {
        foreach (bool performanceMode in new[] { false, true })
        {
            foreach (int displayHz in new[] { 24, 30, 59, 60, 75, 100, 120, 144, 165, 240, 360, 480, 700, 1000 })
            {
                int target = AmbientCaptureCap.TargetHz(displayHz, performanceMode);
                Assert.True(target <= AmbientCaptureCap.CapHz(performanceMode));
                Assert.True(target <= displayHz);
                Assert.True(target > 0);
            }
        }
    }
}
