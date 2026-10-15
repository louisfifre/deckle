namespace Deckle.Core;

// The spacing scale of the design system: the distances the app lays out
// with, in device-independent pixels (96 DPI). One doubling scale from 4 to
// 256 (Louis, 2026-10-16), so a step is always the same number wherever it
// is used and two steps relate by a power of two. A step is a DIP, never a
// physical pixel: at the point where a value meets a screen it goes through
// Dpi.ToPixels with that monitor's scale, so a Large gap is the same hand
// movement, the same visual gap, at 100% as at 250%. Medium, Large and
// XLarge are the first three in use (the taskbar cover's approach levels);
// the other steps are the scale's natural fill, named now so a new distance
// picks a step instead of a number.
public static class Spacing
{
    public const int XXSmall = 4;
    public const int XSmall  = 8;
    public const int Small   = 16;
    public const int Medium  = 32;
    public const int Large   = 64;
    public const int XLarge  = 128;
    public const int XXLarge = 256;
}
