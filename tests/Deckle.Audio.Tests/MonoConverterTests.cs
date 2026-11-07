using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// Pins how device frames become one mono stream: the plain mean of the
// channels, and silence for a sample that is not a finite number.
public class MonoConverterTests
{
    private static float[] Convert(float[] interleaved, int channels, int frames)
    {
        var mono = new float[frames];
        MonoConverter.ToMono(interleaved, channels, frames, mono);
        return mono;
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ToMono_Stereo_IsTheMeanOfBothChannels()
        => Assert.Equal([2f, 3f], Convert([1f, 3f, 2f, 4f], channels: 2, frames: 2));

    [Fact]
    [Trait("Category", "unit")]
    public void ToMono_RightChannelOnly_IsNotSilenced()
        => Assert.Equal([0.25f, 0.5f], Convert([0f, 0.5f, 0f, 1f], channels: 2, frames: 2));

    [Fact]
    [Trait("Category", "unit")]
    public void ToMono_OneChannel_CopiesTheSamples()
        => Assert.Equal([0.1f, -0.2f, 0.3f], Convert([0.1f, -0.2f, 0.3f], channels: 1, frames: 3));

    [Fact]
    [Trait("Category", "unit")]
    public void ToMono_NaNAndInfinity_ReadAsZero()
    {
        float[] stereo = Convert([float.NaN, 1f, float.PositiveInfinity, float.NegativeInfinity], channels: 2, frames: 2);
        float[] mono = Convert([float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0.5f], channels: 1, frames: 4);

        Assert.Equal([0.5f, 0f], stereo);
        Assert.Equal([0f, 0f, 0f, 0.5f], mono);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ToMono_DestinationShorterThanFrames_Throws()
        => Assert.Throws<ArgumentException>(() =>
            MonoConverter.ToMono(new float[8], channels: 2, frames: 4, new float[3]));
}
