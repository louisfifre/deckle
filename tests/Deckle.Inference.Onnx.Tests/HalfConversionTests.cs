using Xunit;

namespace Deckle.Inference.Onnx.Tests;

// The float to half narrowing that Float16 inputs and outputs go through, as pure
// code: no model, no runtime.
[Trait("Category", "unit")]
public class HalfConversionTests
{
    [Fact]
    public void ExactlyRepresentableValuesRoundTrip()
    {
        float[] values = [0f, 1f, -2f, 1.5f, 0.25f, 1024f, -0.125f];

        ushort[] bits = HalfConversion.ToBits(values);

        Assert.Equal(values, bits.Select(b => (float)BitConverter.UInt16BitsToHalf(b)).ToArray());
    }

    [Fact]
    public void KnownValuesMapToTheirIeeeBitPatterns()
    {
        Assert.Equal<ushort[]>([0x3C00, 0xC000, 0x3E00], HalfConversion.ToBits([1f, -2f, 1.5f]));
    }

    [Fact]
    public void ValuesBeyondHalfRangeBecomeInfinity()
    {
        Assert.True(float.IsPositiveInfinity((float)BitConverter.UInt16BitsToHalf(HalfConversion.ToBits([70000f])[0])));
    }

    [Fact]
    public void UnrepresentablePrecisionRoundsToNearestHalf()
    {
        // 1 + 2^-12 is below half's 2^-10 step at 1.0: it rounds back to 1.
        Assert.Equal(1f, (float)BitConverter.UInt16BitsToHalf(HalfConversion.ToBits([1.0002441f])[0]));
    }
}

