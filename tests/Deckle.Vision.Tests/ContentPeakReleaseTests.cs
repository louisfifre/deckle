using Deckle.Vision;
using Xunit;

namespace Deckle.Vision.Tests;

[Trait("Category", "unit")]
public sealed class ContentPeakReleaseTests
{
    [Fact]
    public void ReleaseOverOneFifteenthOfASecondMatchesTheOriginalPerFrameDecay()
    {
        // The per-second constant encodes a decay of 0.97 per frame at 15 Hz.
        Assert.Equal(0.97, FrameSampler.ContentPeakRetention(1.0 / 15), precision: 6);
    }

    [Fact]
    public void ReleaseDependsOnElapsedTimeNotOnFrameCount()
    {
        float twoFastFrames = FrameSampler.ContentPeakRetention(1.0 / 60)
                            * FrameSampler.ContentPeakRetention(1.0 / 60);
        float oneSlowFrame = FrameSampler.ContentPeakRetention(1.0 / 30);

        Assert.Equal(oneSlowFrame, twoFastFrames, 1e-6f);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    public void NoElapsedTimeReleasesNothing(double elapsedSeconds)
        => Assert.Equal(1f, FrameSampler.ContentPeakRetention(elapsedSeconds));
}
