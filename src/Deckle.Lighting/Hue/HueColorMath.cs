namespace Deckle.Lighting;

// Conversion sRGB → Hue's preferred colour representation : CIE 1931
// xy chromaticity + a 1..254 brightness byte. The xy part follows the
// Philips Hue developer guide (Wide Gamut RGB D65 transform) and then
// clips client-side to the lamp's Gamut C triangle to avoid the
// bridge's edge-projection for out-of-gamut points. The brightness
// part deliberately doesn't follow the developer guide.
//
// Why we don't use Y for brightness. Y * 254 from the same transform is
// photometrically correct but sends pure blue at bri=12, barely above
// off. Chromaticity says what colour, brightness says how much of it:
// brightness is max(R, G, B), so a saturated colour reaches bri=254
// whatever its hue.
//
// Pure black (0, 0, 0) returns (0, 0) for xy and brightness 0 so the
// caller knows to send `on:false` instead of pushing a degenerate xy
// value.
internal static class HueColorMath
{
    public static (HueXy Xy, byte Brightness) RgbToHueXyBri(LightColor c)
    {
        if (c.R == 0 && c.G == 0 && c.B == 0)
            return (new HueXy(0, 0), 0);

        double rs = c.R / 255.0;
        double gs = c.G / 255.0;
        double bs = c.B / 255.0;

        double r = SrgbGammaToLinear(rs);
        double g = SrgbGammaToLinear(gs);
        double b = SrgbGammaToLinear(bs);

        // Wide Gamut RGB → XYZ (D65) — Philips Hue reference matrix.
        // Source : developers.meethue.com/develop/application-design-guidance/
        //          color-conversion-formulas-rgb-to-xy-and-back/
        double X = r * 0.664511 + g * 0.154324 + b * 0.162028;
        double Y = r * 0.283881 + g * 0.668433 + b * 0.047685;
        double Z = r * 0.000088 + g * 0.072310 + b * 0.986039;

        double sum = X + Y + Z;
        double xc = sum > 0 ? X / sum : 0;
        double yc = sum > 0 ? Y / sum : 0;

        HueXy clipped = ClipToGamutC(new HueXy(xc, yc));

        // Floor of 1 keeps the lamp lit; pure black took the early-out above.
        int maxByte = Math.Max(c.R, Math.Max(c.G, c.B));
        byte brightness = (byte)Math.Clamp(
            (int)Math.Round(maxByte * 254.0 / 255.0), 1, 254);

        return (clipped, brightness);
    }

    private static double SrgbGammaToLinear(double v)
        => v > 0.04045
            ? Math.Pow((v + 0.055) / 1.055, 2.4)
            : v / 12.92;

    // Gamut C corners (Philips Hue Play / Iris / E14 / Lightstrip Plus
    // gen 2+). Published in the Hue developer docs as the chromaticity
    // primaries the lamp can physically reach. The bridge's own handling
    // of a deep blue just outside the blue corner lands on the B-G edge
    // and renders turquoise; the client-side nearest-edge clip keeps the
    // blue saturated at the blue corner. Projection toward D65 or sigmoid
    // hull compression would desaturate that corner.
    private const double GamutCRedX   = 0.6915, GamutCRedY   = 0.3083;
    private const double GamutCGreenX = 0.17,   GamutCGreenY = 0.7;
    private const double GamutCBlueX  = 0.1532, GamutCBlueY  = 0.0475;

    internal static HueXy ClipToGamutC(HueXy xy)
    {
        double px = xy.X, py = xy.Y;

        if (IsInsideGamutC(px, py))
            return xy;

        var p1 = ClosestPointOnSegment(px, py, GamutCRedX,   GamutCRedY,   GamutCGreenX, GamutCGreenY);
        var p2 = ClosestPointOnSegment(px, py, GamutCGreenX, GamutCGreenY, GamutCBlueX,  GamutCBlueY);
        var p3 = ClosestPointOnSegment(px, py, GamutCBlueX,  GamutCBlueY,  GamutCRedX,   GamutCRedY);

        double d1 = DistanceSquared(px, py, p1.x, p1.y);
        double d2 = DistanceSquared(px, py, p2.x, p2.y);
        double d3 = DistanceSquared(px, py, p3.x, p3.y);

        if (d1 <= d2 && d1 <= d3) return new HueXy(p1.x, p1.y);
        if (d2 <= d3)              return new HueXy(p2.x, p2.y);
        return new HueXy(p3.x, p3.y);
    }

    // A point on an edge (sign == 0) counts as inside, so the clip leaves
    // boundary points unchanged.
    private static bool IsInsideGamutC(double px, double py)
    {
        double d1 = EdgeSign(px, py, GamutCRedX,   GamutCRedY,   GamutCGreenX, GamutCGreenY);
        double d2 = EdgeSign(px, py, GamutCGreenX, GamutCGreenY, GamutCBlueX,  GamutCBlueY);
        double d3 = EdgeSign(px, py, GamutCBlueX,  GamutCBlueY,  GamutCRedX,   GamutCRedY);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static double EdgeSign(double px, double py, double x1, double y1, double x2, double y2)
        => (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);

    private static (double x, double y) ClosestPointOnSegment(
        double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-12) return (ax, ay);
        double t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;
        return (ax + t * dx, ay + t * dy);
    }

    private static double DistanceSquared(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1, dy = y2 - y1;
        return dx * dx + dy * dy;
    }
}

internal readonly record struct HueXy(double X, double Y);
