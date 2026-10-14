// TrayMenuPlacement — on which side of the tray icon the menu opens.

namespace Deckle.Shell.TrayMenu;

/// <summary>The side of its anchor a tray menu opens on.</summary>
internal enum TrayMenuSide
{
    Above,
    Below,
    LeftOf,
    RightOf,
}

// The taskbar is the band of a monitor that lies outside its work area, and
// the tray icon sits in that band. The menu opens on the side of the icon that
// faces the work area: above it for a taskbar at the bottom, to its right for
// a taskbar on the left, and so on. An icon inside the work area (the overflow
// flyout, an auto-hidden bar) opens the menu above, as Windows does. The host
// reads the rects; this type only decides, so the rule is testable without a
// monitor.
internal static class TrayMenuPlacement
{
    /// <summary>
    /// Decides the side from the anchor's center against the work area's
    /// edges. Both rects are in the same pixel space.
    /// </summary>
    /// <param name="anchor">The tray icon rect (left, top, right, bottom).</param>
    /// <param name="workArea">The work area of the monitor under the anchor
    /// (left, top, right, bottom).</param>
    internal static TrayMenuSide Choose(
        (int Left, int Top, int Right, int Bottom) anchor,
        (int Left, int Top, int Right, int Bottom) workArea)
    {
        int centerX = (anchor.Left + anchor.Right) / 2;
        int centerY = (anchor.Top + anchor.Bottom) / 2;

        if (centerY >= workArea.Bottom) return TrayMenuSide.Above;
        if (centerY < workArea.Top) return TrayMenuSide.Below;
        if (centerX >= workArea.Right) return TrayMenuSide.LeftOf;
        if (centerX < workArea.Left) return TrayMenuSide.RightOf;
        return TrayMenuSide.Above;
    }
}
