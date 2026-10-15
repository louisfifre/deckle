namespace Deckle.Core;

// Converts between device-independent pixels, the unit the design system
// speaks (Spacing), and the physical pixels a screen is made of. 96 DPI is
// the reference where one DIP is one pixel, the Windows convention
// (USER_DEFAULT_SCREEN_DPI). The one place the arithmetic lives, so every
// surface scales a DIP the same way.
public static class Dpi
{
    public const uint ReferenceDpi = 96;

    // 1.0 at 96 DPI, 1.5 at 144, 2.5 at 240.
    public static double ScaleOf(uint dpi) => dpi / (double)ReferenceDpi;

    // Rounded to the nearest pixel, halves rounding up: cursor positions and
    // window rects are whole pixels, so a distance set in DIP must land on
    // one, and 7.5 landing on 8 is what a designer expects, not the banker's
    // 8-or-7 Math.Round defaults to.
    public static int ToPixels(int dip, double dpiScale)
        => (int)Math.Round(dip * dpiScale, MidpointRounding.AwayFromZero);
}
