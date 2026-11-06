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

    // Keeps the end of the text within the budget. The head is dropped at a
    // paragraph break when one falls soon after the cut, at a space
    // otherwise, so the oldest visible line never starts mid-word. The text
    // itself comes whole from the engine; this bounds the layout only.
    public static string TrimToBudget(string text, int maxCharacters)
    {
        if (text.Length <= maxCharacters) return text;

        int cut = text.Length - maxCharacters;
        int paragraph = text.IndexOf("\n\n", cut, StringComparison.Ordinal);
        if (paragraph >= 0 && text.Length - paragraph >= maxCharacters / 2)
            return text[(paragraph + 2)..];

        int space = text.IndexOf(' ', cut);
        return space >= 0 && text.Length - space >= maxCharacters / 2
            ? text[(space + 1)..]
            : text[cut..];
    }

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
