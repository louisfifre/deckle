using Deckle.Composition;

namespace Deckle.Lighting.Ambient;

// Per-tick tuning of one target colour, in float. Saturation runs in
// OKLCh on linear light ; the brightness curve and the floor run on the
// encoded channels. Nothing is rounded here : the push quantises once,
// after smoothing.
internal static class AmbientColorPipeline
{
    // The black gate : a source is black only when its unrounded linear
    // mean is exactly (0, 0, 0), i.e. every cell it covers is black. A
    // dark but non-black source is tuned like any other.
    public static bool IsBlackSource((float R, float G, float B) linear)
        => linear.R == 0f && linear.G == 0f && linear.B == 0f;

    // isBlack marks an exactly black source (a linear mean of exactly
    // (0, 0, 0), or a light the user turned off). It yields black when
    // the floor is off and the neutral floor when it is on.
    public static SrgbColor ApplyTuning(
        SrgbColor color,
        bool isBlack,
        double saturationBoost,
        double brightnessCurveX1,
        double brightnessCurveY1,
        double brightnessCurveX2,
        double brightnessCurveY2,
        bool minBrightnessEnabled,
        int minBrightness)
    {
        if (isBlack)
        {
            return minBrightnessEnabled
                ? ApplyMinBrightness(SrgbColor.Black, minBrightness)
                : SrgbColor.Black;
        }

        SrgbColor saturated = ApplySaturationBoost(color, saturationBoost);
        SrgbColor shaped = AmbientBrightnessCurve.Apply(
            saturated,
            brightnessCurveX1,
            brightnessCurveY1,
            brightnessCurveX2,
            brightnessCurveY2);
        return minBrightnessEnabled
            ? ApplyMinBrightness(shaped, minBrightness)
            : shaped;
    }

    private static SrgbColor ApplySaturationBoost(SrgbColor color, double boost)
    {
        if (Math.Abs(boost - 1.0) < 0.001) return color;

        var (rLin, gLin, bLin) = color.ToLinear();
        var (L, C, h) = ColorSpace.LinearRgbToOklch(rLin, gLin, bLin);
        if (C <= 0f) return color;

        float newC = (float)Math.Max(0.0, C * boost);
        return SrgbColor.FromLinear(ColorSpace.OklchToLinearRgb(L, newC, h));
    }

    // A floor of N raises the encoded max channel to N/255 (the byte
    // semantics of MinBrightness), scaling the other channels with it ;
    // black becomes the neutral floor.
    internal static SrgbColor ApplyMinBrightness(SrgbColor color, int minBri)
    {
        int floorByte = Math.Clamp(minBri, 0, 254);
        if (floorByte == 0) return color;

        float floor = floorByte / 255f;
        float max = color.Max;
        if (max <= 0f) return new SrgbColor(floor, floor, floor);
        if (max >= floor) return color;

        return color.Scale(floor / max);
    }
}
