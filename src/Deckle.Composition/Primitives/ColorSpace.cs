using Windows.UI;

namespace Deckle.Composition;

// ── ColorSpace ────────────────────────────────────────────────────────────────
//
// Pure colour-space primitives reusable across the Deckle family. Lives in
// Deckle.Composition because the canonical rotating conic stroke uses
// perceptually-uniform OKLCh interpolation to avoid the luminance
// asymmetry of HSV (yellows ≈ 0.93 luma, blues ≈ 0.07 luma — visible as
// a top/bottom gradient when a Saturation effect drags the stroke
// toward greyscale).
//
// Math is pure: no Composition runtime, no allocations, no IO.
public static class ColorSpace
{
    // OKLCh → sRGB (gamma-corrected, 8-bit channels), channels clamped.
    //
<<<<<<< HEAD
    // L/C combinations outside the sRGB gamut give linear values outside
    // [0, 1]. The clamp on the gamma output flattens those hues gently
    // rather than stopping hard, which is good enough for a rotating conic
    // wheel where no individual hue lingers.
=======
    // Pipeline: OKLCh → OKLab → linear sRGB → gamma-corrected sRGB.
    // Linear sRGB values fall outside [0, 1] for L/C combinations that
    // exit the sRGB gamut (yellows and blues at L=0.75 start clipping
    // around C ≈ 0.18). This conversion clamps each channel to [0, 1] on
    // the gamma output: the chroma is kept as far as it goes and the
    // lightness is lost — a clipped red at L 0.75 / C 0.3 desaturates to
    // a grey near 0.24 where cyan gives 0.69. Fine for a swatch that must
    // stay as saturated as asked; wrong for a wheel that must keep one
    // lightness across hues, which is OklchToRgbInGamut below.
>>>>>>> main
    //
    // L is OKLab lightness in [0, 1], C is OKLab chroma in about [0, 0.4],
    // hTurns is hue in turns in [0, 1]: 0 red, 0.25 yellow, 0.5 green,
    // 0.75 blue (approximate, OKLab hue ordering).
    public static Color OklchToRgb(float L, float C, float hTurns)
    {
        var (rLin, gLin, bLin) = OklchToLinearRgb(L, C, hTurns);

        return Color.FromArgb(
            0xFF,
            (byte)MathF.Round(Math.Clamp(LinearToSrgb(rLin), 0f, 1f) * 255f),
            (byte)MathF.Round(Math.Clamp(LinearToSrgb(gLin), 0f, 1f) * 255f),
            (byte)MathF.Round(Math.Clamp(LinearToSrgb(bLin), 0f, 1f) * 255f));
    }

<<<<<<< HEAD
    // sRGB 8-bit → OKLCh, the inverse of OklchToRgb. Hue is returned in
    // turns in [0, 1), like OklchToRgb takes it.
=======
    // OKLCh → sRGB with the chroma lowered per hue until the colour fits
    // the sRGB gamut, lightness and hue kept. This is the promise the HUD's
    // cone is built on: one lightness for every hue, so one grey once a
    // Saturation effect desaturates it, and in colour each hue at the
    // largest chroma the display can show at that lightness — the CSS
    // Color 4 gamut-mapping idea, by chroma reduction alone. The clamping
    // conversion above broke that promise on the greyscale cone (2026-10-13):
    // at L 0.75 / C 0.3 no sRGB hue carries the chroma, every channel was
    // clipped, and the greys ran from 0.24 to 0.69 around the wheel. The
    // chroma argument is therefore a ceiling, not a value every hue reaches.
    public static Color OklchToRgbInGamut(float L, float C, float hTurns)
    {
        var (rLin, gLin, bLin) = OklchToLinearRgbInGamut(L, C, hTurns);

        return Color.FromArgb(
            0xFF,
            (byte)MathF.Round(LinearToSrgb(rLin) * 255f),
            (byte)MathF.Round(LinearToSrgb(gLin) * 255f),
            (byte)MathF.Round(LinearToSrgb(bLin) * 255f));
    }

    // The linear-light body of OklchToRgbInGamut. A binary search on the
    // chroma between 0 — the grey at L, in gamut once L is clamped to
    // [0, 1] — and the requested value; 16 halvings land within 2⁻¹⁶ of the
    // boundary, far below 8-bit resolution. The channels come back clamped
    // so float noise at the boundary cannot leak a stray −1e-7.
    public static (float R, float G, float B) OklchToLinearRgbInGamut(float L, float C, float hTurns)
    {
        float lightness = Math.Clamp(L, 0f, 1f);
        float ceiling = MathF.Max(0f, C);
        var rgb = OklchToLinearRgb(lightness, ceiling, hTurns);
        if (!IsInSrgbGamut(rgb))
        {
            float inside = 0f, outside = ceiling;
            for (int i = 0; i < 16; i++)
            {
                float middle = (inside + outside) / 2f;
                if (IsInSrgbGamut(OklchToLinearRgb(lightness, middle, hTurns))) inside = middle;
                else outside = middle;
            }
            rgb = OklchToLinearRgb(lightness, inside, hTurns);
        }
        return (Math.Clamp(rgb.R, 0f, 1f), Math.Clamp(rgb.G, 0f, 1f), Math.Clamp(rgb.B, 0f, 1f));
    }

    // Float noise around the gamut boundary is not a colour out of gamut.
    private const float GamutTolerance = 1e-5f;

    private static bool IsInSrgbGamut((float R, float G, float B) c)
        => c.R >= -GamutTolerance && c.R <= 1f + GamutTolerance
        && c.G >= -GamutTolerance && c.G <= 1f + GamutTolerance
        && c.B >= -GamutTolerance && c.B <= 1f + GamutTolerance;

    // sRGB 8-bit → OKLCh — inverse of OklchToRgb. Same pipeline run
    // backwards : sRGB byte → linear via the LUT → cone responses via
    // the forward Björn Ottosson matrix → cube root → OKLab via the
    // forward matrix → polar (C, h) in the a-b plane.
>>>>>>> main
    //
    // A perfectly grey input (r = g = b) gives C = 0 and a degenerate h
    // (Atan2(0, 0) returns 0 in .NET): treat C == 0 as "hue irrelevant"
    // rather than read h.
    public static (float L, float C, float h) RgbToOklch(byte r, byte g, byte b)
        => LinearRgbToOklch(SrgbToLinear8Lut[r], SrgbToLinear8Lut[g], SrgbToLinear8Lut[b]);

    // sRGB OETF (IEC 61966-2-1). Negative inputs are mirrored through the
    // curve, which gives a symmetric result that clamps cleanly to 0
    // afterwards.
    public static float LinearToSrgb(float x)
    {
        if (x < 0f) return -LinearToSrgb(-x);
        return x <= 0.0031308f
            ? 12.92f * x
            : 1.055f * MathF.Pow(x, 1f / 2.4f) - 0.055f;
    }

    // sRGB EOTF, the reciprocal of LinearToSrgb. Negative inputs are
    // mirrored through the curve like LinearToSrgb does, so round-trips
    // stay numerically stable for the small-magnitude negatives that
    // scRGB allows.
    public static float SrgbToLinear(float x)
    {
        if (x < 0f) return -SrgbToLinear(-x);
        return x <= 0.04045f
            ? x / 12.92f
            : MathF.Pow((x + 0.055f) / 1.055f, 2.4f);
    }

    // SrgbToLinear for every byte value. Averaging pixels must happen in
    // linear light: sRGB encodes luminance through a ~2.4 gamma, so equal
    // byte deltas are not equal light deltas and a sum of raw bytes is
    // biased. One lookup replaces a Pow per pixel.
    public static readonly float[] SrgbToLinear8Lut = InitSrgbToLinear8Lut();

    private static float[] InitSrgbToLinear8Lut()
    {
        var lut = new float[256];
        for (int i = 0; i < 256; i++)
            lut[i] = SrgbToLinear(i / 255f);
        return lut;
    }

    // scRGB FP16 → sRGB 8-bit, content-relative Hable filmic tone-map.
    //
    // scRGB (the FP16 format DXGI Output Duplication returns when the OS
    // is in HDR mode) is linear light where (1, 1, 1) is the SDR
    // reference white (80 nits) and values above 1 are HDR highlights up
    // to the display's peakWhite. Negatives (primaries beyond sRGB) are
    // allowed; they are clipped after the tone-map.
    //
    // The curve normalises on contentPeak, the caller's rolling max of
    // recent frames' max channel in scRGB units, not on the display peak
    // (peakWhite / 80): a typical HDR scene peaks far below the display,
    // and normalising on the display squashes it into the bottom of the
    // output range. The caller caps that rolling max at peakWhite / 80 so
    // one glint pixel cannot crush the whole scene below it.
    //
    // Hable's Uncharted-2 operator compresses highlights without crushing
    // the mid-tones; F(rgb) / F(W) maps scRGB 0 → 0 and scRGB W → 1.
    //
    // exposureEv is a linear-light bias in EV stops applied before the
    // tone-map: +1 doubles brightness, -1 halves it, 0 is neutral.
    public static Color ScRgbToSrgb(float r, float g, float b, float contentPeak, double exposureEv = 0.0)
    {
        float gain = MathF.Pow(2f, (float)exposureEv);
        float rE = r * gain;
        float gE = g * gain;
        float bE = b * gain;

        // SDR floor: on a dark frame the caller's rolling max collapses
        // toward 0. Normalising below scRGB 1.0 would amplify noise, and
        // Hable(0) = 0 would divide by zero.
        float W = MathF.Max(contentPeak, 1f);
        float invHableW = 1f / Hable(W);

        float rN = Hable(rE) * invHableW;
        float gN = Hable(gE) * invHableW;
        float bN = Hable(bE) * invHableW;

        return Color.FromArgb(
            0xFF,
            (byte)MathF.Round(LinearToSrgb(Math.Clamp(rN, 0f, 1f)) * 255f),
            (byte)MathF.Round(LinearToSrgb(Math.Clamp(gN, 0f, 1f)) * 255f),
            (byte)MathF.Round(LinearToSrgb(Math.Clamp(bN, 0f, 1f)) * 255f));
    }

    // Hable's Uncharted-2 filmic tone-mapper (presented at GDC 2010).
    // The constants are the original publication's "shoulder strength"
    // / "linear strength" / "linear angle" / "toe strength" / "toe
    // numerator" / "toe denominator" — widely accepted defaults for
    // HDR-to-LDR mapping. F(0) = 0 algebraically (the E/F bias cancels
    // the toe at the origin).
    public static float Hable(float x)
    {
        const float A = 0.15f, B = 0.50f, C = 0.10f, D = 0.20f, E = 0.02f, F = 0.30f;
        return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
    }

    // Linear sRGB (float) → OKLCh. The body of RgbToOklch without the
    // byte decode, for callers that already hold linear light and must
    // not round it to 8 bits first (the ambient colour path computes in
    // float and quantises once at its output). Same conventions : hue
    // in turns ∈ [0, 1), C == 0 means "hue irrelevant".
    public static (float L, float C, float h) LinearRgbToOklch(float rLin, float gLin, float bLin)
    {
        // Linear sRGB → linear cone responses (forward, Björn
        // Ottosson's matrix — inverse of the OklchToRgb step).
        float lLin = 0.4122214708f * rLin + 0.5363325363f * gLin + 0.0514459929f * bLin;
        float mLin = 0.2119034982f * rLin + 0.6806995451f * gLin + 0.1073969566f * bLin;
        float sLin = 0.0883024619f * rLin + 0.2817188376f * gLin + 0.6299787005f * bLin;

        // Cbrt, not Pow(x, 1/3): negative cone responses do not occur for
        // in-gamut sRGB input, but Cbrt stays defined for them.
        float l_ = MathF.Cbrt(lLin);
        float m_ = MathF.Cbrt(mLin);
        float s_ = MathF.Cbrt(sLin);

        // Cone responses → OKLab (forward matrix).
        float L    = 0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_;
        float a    = 1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_;
        float bLab = 0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_;

        float C = MathF.Sqrt(a * a + bLab * bLab);
        float hRad = MathF.Atan2(bLab, a);
        float h = hRad / MathF.Tau;
        if (h < 0f) h += 1f;

        return (L, C, h);
    }

    // OKLCh → linear sRGB (float). The body of OklchToRgb without the
    // gamma encode and the 8-bit rounding. Out-of-gamut combinations
    // return values outside [0, 1] ; clamping is the caller's choice.
    public static (float R, float G, float B) OklchToLinearRgb(float L, float C, float hTurns)
    {
        float hRad = hTurns * MathF.Tau;
        float a    = C * MathF.Cos(hRad);
        float b    = C * MathF.Sin(hRad);

        // OKLab → non-linear cone responses (Björn Ottosson's matrix).
        float l_ = L + 0.3963377774f * a + 0.2158037573f * b;
        float m_ = L - 0.1055613458f * a - 0.0638541728f * b;
        float s_ = L - 0.0894841775f * a - 1.2914855480f * b;

        float lc = l_ * l_ * l_;
        float mc = m_ * m_ * m_;
        float sc = s_ * s_ * s_;

        // Cone responses → linear sRGB.
        float rLin = +4.0767416621f * lc - 3.3077115913f * mc + 0.2309699292f * sc;
        float gLin = -1.2684380046f * lc + 2.6097574011f * mc - 0.3413193965f * sc;
        float bLin = -0.0041960863f * lc - 0.7034186147f * mc + 1.7076147010f * sc;

        return (rLin, gLin, bLin);
    }
}
