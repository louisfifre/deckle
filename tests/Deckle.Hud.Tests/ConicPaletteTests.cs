using System;
using Deckle.Composition;
using Xunit;

namespace Deckle.Hud.Tests;

// The cone's promise: at one lightness, every hue desaturates to one grey.
// sRGB cannot show every hue at the chroma the config asks for, so the
// in-gamut conversion lowers the chroma per hue and keeps the lightness.
// The clamping conversion kept the chroma instead and spread the greys of
// the default wheel from about 0.24 to 0.69 (bug note beside this file).
public sealed class ConicPaletteTests
{
    private const float LightnessTolerance = 0.002f;

    [Fact]
    [Trait("Category", "regression")]
    public void EveryHueKeepsTheRequestedLightnessWithinTheGamut()
    {
        var config = new HudComposition.ConicArcStrokeConfig();

        for (int i = 0; i < 36; i++)
        {
            float hue = i / 36f;
            var (r, g, b) = ColorSpace.OklchToLinearRgbInGamut(
                config.OklchLightness, config.OklchChroma, hue);

            Assert.InRange(r, 0f, 1f);
            Assert.InRange(g, 0f, 1f);
            Assert.InRange(b, 0f, 1f);

            var (lightness, _, _) = ColorSpace.LinearRgbToOklch(r, g, b);
            Assert.InRange(MathF.Abs(lightness - config.OklchLightness), 0f, LightnessTolerance);
        }
    }

    // A chroma the display can show is returned untouched: the mapping only
    // acts past the gamut boundary.
    [Fact]
    public void AnInGamutColourIsNotChanged()
    {
        var clamped = ColorSpace.OklchToLinearRgb(0.75f, 0.05f, 0.1f);
        var mapped = ColorSpace.OklchToLinearRgbInGamut(0.75f, 0.05f, 0.1f);

        Assert.InRange(MathF.Abs(mapped.R - clamped.R), 0f, 1e-6f);
        Assert.InRange(MathF.Abs(mapped.G - clamped.G), 0f, 1e-6f);
        Assert.InRange(MathF.Abs(mapped.B - clamped.B), 0f, 1e-6f);
    }
}
