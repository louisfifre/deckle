using System.Text;

namespace Deckle.Paste;

// The beginning of a pasted text, as its row shows it under the file name:
// one line, its runs of spaces and line breaks made one space, and cut with
// an ellipsis past MaxLength. The row trims it again to its width; the cut
// here only bounds what a long text keeps in memory.
public static class PasteExcerpt
{
    public const int MaxLength = 160;

    public static string Of(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var line = new StringBuilder(Math.Min(text.Length, MaxLength + 1));
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = line.Length > 0;
                continue;
            }
            if (space) line.Append(' ');
            space = false;
            line.Append(c);
            if (line.Length > MaxLength) break;
        }

        if (line.Length <= MaxLength) return line.ToString();

        // Cut before the limit, never between the two halves of a character
        // outside the basic plane, and without a space before the ellipsis.
        int cut = MaxLength - 1;
        if (char.IsHighSurrogate(line[cut - 1])) cut--;
        return line.ToString(0, cut).TrimEnd() + "…";
    }
}
