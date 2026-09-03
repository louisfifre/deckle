using Deckle.Composition;

namespace Deckle.Lighting.Ambient;

internal static class AmbientColorPipeline
{
    public static (byte R, byte G, byte B) ApplyTuning(
        byte r,
        byte g,
        byte b,
        bool isDark,
        double saturationBoost,
        double brightnessCurveX1,
        double brightnessCurveY1,
        double brightnessCurveX2,
        double brightnessCurveY2,
        bool minBrightnessEnabled,
        int minBrightness)
    {
        if (isDark && !minBrightnessEnabled) return (0, 0, 0);

        (byte sR, byte sG, byte sB) = ApplySaturationBoost(r, g, b, saturationBoost);
        (byte cR, byte cG, byte cB) = AmbientBrightnessCurve.Apply(
            sR,
            sG,
            sB,
            brightnessCurveX1,
            brightnessCurveY1,
            brightnessCurveX2,
            brightnessCurveY2);
        return minBrightnessEnabled
            ? ApplyMinBrightness(cR, cG, cB, minBrightness)
            : (cR, cG, cB);
    }

    private static (byte R, byte G, byte B) ApplySaturationBoost(byte r, byte g, byte b, double boost)
    {
        if (Math.Abs(boost - 1.0) < 0.001) return (r, g, b);

        var (L, C, h) = ColorSpace.RgbToOklch(r, g, b);
        if (C <= 0f) return (r, g, b);

        float newC = (float)Math.Max(0.0, C * boost);
        var result = ColorSpace.OklchToRgb(L, newC, h);
        return (result.R, result.G, result.B);
    }

    internal static (byte R, byte G, byte B) ApplyMinBrightness(byte r, byte g, byte b, int minBri)
    {
        int floor = Math.Clamp(minBri, 0, 254);
        if (floor == 0) return (r, g, b);

        int max = Math.Max(r, Math.Max(g, b));
        if (max == 0)
        {
            byte neutral = (byte)floor;
            return (neutral, neutral, neutral);
        }
        if (max >= floor) return (r, g, b);

        double scale = floor / (double)max;
        return (
            (byte)Math.Min(255, Math.Round(r * scale)),
            (byte)Math.Min(255, Math.Round(g * scale)),
            (byte)Math.Min(255, Math.Round(b * scale)));
    }
}
