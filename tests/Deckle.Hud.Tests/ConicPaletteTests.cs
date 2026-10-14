using System;
using Deckle.Composition;
using Windows.UI;
using Xunit;

namespace Deckle.Hud.Tests;

// The cone's two materials. The grey the black-and-white variants show is a
// flat colour from one lightness, never the colour wheel desaturated: the
// desaturated wheel once carried a dark red band into the countdown gauge
// (bug note beside this file). The wheel itself is baked by the clamping
// conversion, so a hue past the sRGB gamut darkens rather than bleaches.
public sealed class ConicPaletteTests
{
    [Fact]
    [Trait("Category", "regression")]
    public void TheGreyMaterialIsOneGreyAtAnyLightness()
    {
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), HudComposition.GreyMaterialColor(1f));
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x00, 0x00), HudComposition.GreyMaterialColor(0f));

        for (int i = 0; i <= 20; i++)
        {
            var grey = HudComposition.GreyMaterialColor(i / 20f);
            Assert.Equal(0xFF, grey.A);
            Assert.Equal(grey.R, grey.G);
            Assert.Equal(grey.G, grey.B);
        }
    }

    // Perceptual steps: the middle of the range is a mid grey to the eye,
    // which in sRGB bytes sits well above 128.
    [Fact]
    public void TheGreyMaterialRisesWithLightness()
    {
        byte previous = 0;
        for (int i = 1; i <= 10; i++)
        {
            byte value = HudComposition.GreyMaterialColor(i / 10f).R;
            Assert.True(value > previous, $"lightness {i / 10f}: {value} is not above {previous}");
            previous = value;
        }
        Assert.InRange(HudComposition.GreyMaterialColor(0.5f).R, 90, 140);
    }

    // The default wheel asks more chroma than sRGB carries at L 0.75. The
    // clamp keeps the red saturated and lets it darken; the gamut mapping
    // would keep the lightness and lose the chroma. This pins the choice.
    [Fact]
    public void TheWheelKeepsItsRedSaturatedPastTheGamut()
    {
        var config = new HudComposition.ConicArcStrokeConfig();
        var red = ColorSpace.OklchToRgb(config.OklchLightness, config.OklchChroma, 0.08f);

        Assert.True(red.R > 200, $"red channel {red.R} is not saturated");
        Assert.True(red.G < 80 && red.B < 120, $"red carries too much green ({red.G}) or blue ({red.B})");
    }

    // The gamut-mapped conversion stays a pure primitive: lightness held
    // for every hue, channels inside [0, 1], untouched when already in gamut.
    [Fact]
    public void TheInGamutConversionHoldsTheLightnessForEveryHue()
    {
        const float tolerance = 0.002f;
        for (int i = 0; i < 36; i++)
        {
            var (r, g, b) = ColorSpace.OklchToLinearRgbInGamut(0.75f, 0.3f, i / 36f);
            Assert.InRange(r, 0f, 1f);
            Assert.InRange(g, 0f, 1f);
            Assert.InRange(b, 0f, 1f);
            var (lightness, _, _) = ColorSpace.LinearRgbToOklch(r, g, b);
            Assert.InRange(MathF.Abs(lightness - 0.75f), 0f, tolerance);
        }

        var clamped = ColorSpace.OklchToLinearRgb(0.75f, 0.05f, 0.1f);
        var mapped = ColorSpace.OklchToLinearRgbInGamut(0.75f, 0.05f, 0.1f);
        Assert.InRange(MathF.Abs(mapped.R - clamped.R), 0f, 1e-6f);
        Assert.InRange(MathF.Abs(mapped.G - clamped.G), 0f, 1e-6f);
        Assert.InRange(MathF.Abs(mapped.B - clamped.B), 0f, 1e-6f);
    }
}
