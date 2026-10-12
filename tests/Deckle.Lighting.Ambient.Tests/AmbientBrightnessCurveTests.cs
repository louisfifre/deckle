using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientBrightnessCurveTests
{
    [Fact]
    public void LinearBezierLeavesColourUnchangedWhenFloorDisabled()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            Encoded(50, 100, 25),
            isBlack: false,
            saturationBoost: 1.0,
            brightnessCurveX1: 1.0 / 3.0,
            brightnessCurveY1: 1.0 / 3.0,
            brightnessCurveX2: 2.0 / 3.0,
            brightnessCurveY2: 2.0 / 3.0,
            minBrightnessEnabled: false,
            minBrightness: 180);

        Assert.Equal(new LightColor(50, 100, 25), tuned.Quantize());
    }

    [Fact]
    public void MinimumBrightnessDisabledLeavesDimColourDim()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            Encoded(10, 5, 0),
            isBlack: false,
            saturationBoost: 1.0,
            brightnessCurveX1: 1.0 / 3.0,
            brightnessCurveY1: 1.0 / 3.0,
            brightnessCurveX2: 2.0 / 3.0,
            brightnessCurveY2: 2.0 / 3.0,
            minBrightnessEnabled: false,
            minBrightness: 100);

        Assert.Equal(new LightColor(10, 5, 0), tuned.Quantize());
    }

    [Fact]
    public void MinimumBrightnessEnabledRaisesDimColourFloor()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            Encoded(10, 5, 0),
            isBlack: false,
            saturationBoost: 1.0,
            brightnessCurveX1: 1.0 / 3.0,
            brightnessCurveY1: 1.0 / 3.0,
            brightnessCurveX2: 2.0 / 3.0,
            brightnessCurveY2: 2.0 / 3.0,
            minBrightnessEnabled: true,
            minBrightness: 100);

        Assert.Equal(new LightColor(100, 50, 0), tuned.Quantize());
    }

    [Fact]
    [Trait("Category", "regression")]
    public void MinimumBrightnessEnabledKeepsBlackSampleAtFloor()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            SrgbColor.FromLinear(0f, 0f, 0f),
            isBlack: true,
            saturationBoost: 1.0,
            brightnessCurveX1: 0.18,
            brightnessCurveY1: 0.55,
            brightnessCurveX2: 0.40,
            brightnessCurveY2: 0.90,
            minBrightnessEnabled: true,
            minBrightness: 90);

        Assert.Equal(new LightColor(90, 90, 90), tuned.Quantize());
    }

    [Fact]
    public void MinimumBrightnessDisabledAllowsBlackSample()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            SrgbColor.FromLinear(0f, 0f, 0f),
            isBlack: true,
            saturationBoost: 1.0,
            brightnessCurveX1: 0.18,
            brightnessCurveY1: 0.55,
            brightnessCurveX2: 0.40,
            brightnessCurveY2: 0.90,
            minBrightnessEnabled: false,
            minBrightness: 90);

        Assert.Equal(LightColor.Black, tuned.Quantize());
    }

    private static SrgbColor Encoded(byte r, byte g, byte b)
        => new(r / 255f, g / 255f, b / 255f);
}
