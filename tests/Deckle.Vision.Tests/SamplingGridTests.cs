using Xunit;

namespace Deckle.Vision.Tests;

// The Ambient preview draws the sampling grid from the display's size alone,
// before any capture runs. These pin the grid FrameSampler.GridFor promises,
// which is the one its constructor builds from the same halving.
[Trait("Category", "unit")]
public sealed class SamplingGridTests
{
    [Theory]
    [InlineData(1920, 1080, 60, 33)]
    [InlineData(2560, 1440, 40, 22)]
    [InlineData(3840, 2160, 60, 33)]
    [InlineData(3440, 1440, 53, 22)]
    public void AGridKeepsTheSourceRatioAboveTheTarget(int width, int height, int cols, int rows)
    {
        Assert.Equal((cols, rows), FrameSampler.GridFor(width, height));
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    public void AnEmptySourceHasNoGrid(int width, int height)
    {
        Assert.Equal((0, 0), FrameSampler.GridFor(width, height));
    }
}
