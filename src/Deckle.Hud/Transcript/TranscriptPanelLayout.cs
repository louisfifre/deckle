using System;
using Windows.Graphics;

namespace Deckle.Hud;

// Geometry of the live transcript panel. Everything is in dips until the
// final rect, which is in physical px for AppWindow.MoveAndResize. The bottom
// edge is anchored so the panel grows upward as lines arrive, and its height
// is always padding plus a whole number of lines so the top edge never cuts
// a line in half.
internal static class TranscriptPanelLayout
{
    // Lines that fit between the top and bottom margins once the padding is
    // taken out. At least one, so a tiny work area still shows the newest line.
    public static int MaxVisibleLines(int workAreaHeightDip, int edgeMarginDip, int verticalPaddingDip, double lineHeightDip)
    {
        double available = workAreaHeightDip - 2 * edgeMarginDip - verticalPaddingDip;
        return Math.Max(1, (int)Math.Floor(available / lineHeightDip));
    }

    public static int VisibleLines(int contentLines, int maxLines) =>
        Math.Max(1, Math.Min(contentLines, maxLines));

    public static RectInt32 ComputeRectPx(
        RectInt32 workAreaPx, double dpiScale, bool rightSide,
        int widthDip, int edgeMarginDip, int verticalPaddingDip, double lineHeightDip, int visibleLines)
    {
        int height = (int)Math.Round((verticalPaddingDip + visibleLines * lineHeightDip) * dpiScale);
        int width = (int)Math.Round(widthDip * dpiScale);
        int margin = (int)Math.Round(edgeMarginDip * dpiScale);

        int x = rightSide
            ? workAreaPx.X + workAreaPx.Width - margin - width
            : workAreaPx.X + margin;
        int y = workAreaPx.Y + workAreaPx.Height - margin - height;

        return new RectInt32(x, y, width, height);
    }
}
