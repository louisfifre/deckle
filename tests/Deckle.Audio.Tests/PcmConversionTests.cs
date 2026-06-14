using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

// FloatToPcm16 is the render-side conversion every spoken clip passes through
// before waveOut. Pure and load-bearing, so it's pinned here. The inverse
// (PcmToFloat) is exercised by the capture path; the round-trip below covers
// both directions agreeing within 16-bit quantization.
public class PcmConversionTests
{
    [Fact]
    [Trait("Category", "unit")]
    public void FloatToPcm16_ProducesTwoBytesPerSample()
    {
        var samples = new float[] { 0f, 0.5f, -0.5f, 1f };
        byte[] pcm = PcmConversion.FloatToPcm16(samples);
        Assert.Equal(samples.Length * 2, pcm.Length);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void FloatToPcm16_RoundTripsThroughPcmToFloat()
    {
        var samples = new float[] { 0f, 0.25f, -0.25f, 0.5f, -0.5f, 0.75f, -0.75f };
        byte[] pcm = PcmConversion.FloatToPcm16(samples);
        float[] back = PcmConversion.PcmToFloat(pcm);

        Assert.Equal(samples.Length, back.Length);
        for (int i = 0; i < samples.Length; i++)
            Assert.Equal(samples[i], (double)back[i], 3); // 16-bit quantization tolerance
    }

    [Theory]
    [Trait("Category", "unit")]
    [InlineData(2f, 1.0)]    // above full-scale clamps to +1
    [InlineData(-2f, -1.0)]  // below full-scale clamps to -1
    public void FloatToPcm16_ClampsOutOfRange(float input, double expectedApprox)
    {
        byte[] pcm = PcmConversion.FloatToPcm16(new[] { input });
        double back = PcmConversion.PcmToFloat(pcm)[0];
        Assert.Equal(expectedApprox, back, 2);
    }
}
