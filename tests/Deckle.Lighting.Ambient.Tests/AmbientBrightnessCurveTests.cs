using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientBrightnessCurveTests
{
    [Fact]
    public void LinearBezierLeavesColourUnchangedWhenFloorDisabled()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            50,
            100,
            25,
            isDark: false,
            saturationBoost: 1.0,
            brightnessCurveX1: 1.0 / 3.0,
            brightnessCurveY1: 1.0 / 3.0,
            brightnessCurveX2: 2.0 / 3.0,
            brightnessCurveY2: 2.0 / 3.0,
            minBrightnessEnabled: false,
            minBrightness: 180);

        Assert.Equal((50, 100, 25), tuned);
    }

    [Fact]
    public void MinimumBrightnessDisabledLeavesDimColourDim()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            10,
            5,
            0,
            isDark: false,
            saturationBoost: 1.0,
            brightnessCurveX1: 1.0 / 3.0,
            brightnessCurveY1: 1.0 / 3.0,
            brightnessCurveX2: 2.0 / 3.0,
            brightnessCurveY2: 2.0 / 3.0,
            minBrightnessEnabled: false,
            minBrightness: 100);

        Assert.Equal((10, 5, 0), tuned);
    }

    [Fact]
    public void MinimumBrightnessEnabledRaisesDimColourFloor()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            10,
            5,
            0,
            isDark: false,
            saturationBoost: 1.0,
            brightnessCurveX1: 1.0 / 3.0,
            brightnessCurveY1: 1.0 / 3.0,
            brightnessCurveX2: 2.0 / 3.0,
            brightnessCurveY2: 2.0 / 3.0,
            minBrightnessEnabled: true,
            minBrightness: 100);

        Assert.Equal((100, 50, 0), tuned);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void MinimumBrightnessEnabledKeepsBlackSampleAtFloor()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            0,
            0,
            0,
            isDark: true,
            saturationBoost: 1.0,
            brightnessCurveX1: 0.18,
            brightnessCurveY1: 0.55,
            brightnessCurveX2: 0.40,
            brightnessCurveY2: 0.90,
            minBrightnessEnabled: true,
            minBrightness: 90);

        Assert.Equal((90, 90, 90), tuned);
    }

    [Fact]
    public void MinimumBrightnessDisabledAllowsBlackSample()
    {
        var tuned = AmbientColorPipeline.ApplyTuning(
            0,
            0,
            0,
            isDark: true,
            saturationBoost: 1.0,
            brightnessCurveX1: 0.18,
            brightnessCurveY1: 0.55,
            brightnessCurveX2: 0.40,
            brightnessCurveY2: 0.90,
            minBrightnessEnabled: false,
            minBrightness: 90);

        Assert.Equal((0, 0, 0), tuned);
    }

    [Fact]
    public void AmbientPresetAppliesBezierCurveCoordinates()
    {
        var settings = new AmbientSettings();

        AmbientModePresets.Apply(AmbientMode.Ambient, settings);

        Assert.Equal(0.18, settings.BrightnessCurveX1);
        Assert.Equal(0.55, settings.BrightnessCurveY1);
        Assert.Equal(0.40, settings.BrightnessCurveX2);
        Assert.Equal(0.90, settings.BrightnessCurveY2);
    }
}
