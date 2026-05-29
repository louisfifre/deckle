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
        BrightnessCurveType brightnessCurveType,
        double brightnessCurveParam,
        double brightnessCurveSCurveSteepness,
        int minBrightness)
    {
        if (isDark) return (0, 0, 0);

        (byte sR, byte sG, byte sB) = ApplySaturationBoost(r, g, b, saturationBoost);
        double param = brightnessCurveType switch
        {
            BrightnessCurveType.Gamma  => brightnessCurveParam,
            BrightnessCurveType.SCurve => brightnessCurveSCurveSteepness,
            _                          => 0.0,
        };
        (byte cR, byte cG, byte cB) = ApplyBrightnessCurve(sR, sG, sB, brightnessCurveType, param);
        return ApplyMinBrightness(cR, cG, cB, minBrightness);
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

    private static (byte R, byte G, byte B) ApplyBrightnessCurve(byte r, byte g, byte b, BrightnessCurveType type, double param)
    {
        int max = Math.Max(r, Math.Max(g, b));
        if (max == 0) return (r, g, b);

        double ratio = max / 255.0;
        double y;
        switch (type)
        {
            case BrightnessCurveType.Linear:
                return (r, g, b);

            case BrightnessCurveType.Gamma:
                if (Math.Abs(param - 1.0) < 0.001) return (r, g, b);
                y = Math.Pow(ratio, param);
                break;

            case BrightnessCurveType.SCurve:
                if (Math.Abs(param) < 0.05) return (r, g, b);
                double k = Math.Abs(param);
                double a = 1.0 / (1.0 + Math.Exp(0.5 * k));
                double bN = 1.0 / (1.0 + Math.Exp(-0.5 * k));
                double raw = 1.0 / (1.0 + Math.Exp(-k * (ratio - 0.5)));
                y = (raw - a) / (bN - a);
                if (param < 0.0) y = 2.0 * ratio - y;
                break;

            case BrightnessCurveType.Logarithmic:
                y = Math.Log10(1.0 + 9.0 * ratio);
                break;

            default:
                return (r, g, b);
        }

        double scale = y / ratio;
        return (
            (byte)Math.Clamp((int)Math.Round(r * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(g * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(b * scale), 0, 255));
    }

    private static (byte R, byte G, byte B) ApplyMinBrightness(byte r, byte g, byte b, int minBri)
    {
        if (minBri <= 0) return (r, g, b);

        int max = Math.Max(r, Math.Max(g, b));
        if (max == 0 || max >= minBri) return (r, g, b);

        double scale = minBri / (double)max;
        return (
            (byte)Math.Min(255, Math.Round(r * scale)),
            (byte)Math.Min(255, Math.Round(g * scale)),
            (byte)Math.Min(255, Math.Round(b * scale)));
    }
}
