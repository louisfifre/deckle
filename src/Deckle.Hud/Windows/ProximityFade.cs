using System;

namespace Deckle.Hud;

// The proximity fade curve shared by the HUD surfaces: cursor distance to a
// window rect mapped to a layered-window alpha through smoothstep. Kept pure
// so every surface fades with the same curve and the curve is testable
// without a window. The endpoint alphas are parameters because the HUD fades
// down as the cursor approaches while the transcript panel fades up.
internal static class ProximityFade
{
    // Euclidean distance from the cursor to the nearest point of the rect,
    // 0 when the cursor is inside it. Edges count as inside.
    public static double DistanceToRectPx(int cursorX, int cursorY, int left, int top, int right, int bottom)
    {
        int dx = cursorX < left ? left - cursorX : cursorX > right ? cursorX - right : 0;
        int dy = cursorY < top ? top - cursorY : cursorY > bottom ? cursorY - bottom : 0;
        return Math.Sqrt((double)dx * dx + (double)dy * dy);
    }

    // nearAlpha at or below the near radius, farAlpha at or beyond the far
    // radius, smoothstep in between. Radii are in dips and scaled to px so the
    // fade feels the same on every display.
    public static byte Alpha(double distancePx, double dpiScale, double nearDip, double farDip, byte nearAlpha, byte farAlpha)
    {
        double nearPx = nearDip * dpiScale;
        double farPx = farDip * dpiScale;
        double t = Math.Clamp((distancePx - nearPx) / (farPx - nearPx), 0.0, 1.0);
        double eased = t * t * (3.0 - 2.0 * t);
        return (byte)Math.Round(nearAlpha + eased * (farAlpha - nearAlpha));
    }
}
