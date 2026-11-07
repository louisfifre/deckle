using System.Globalization;

namespace Deckle.TestSupport;

// ── TableTemplate ─────────────────────────────────────────────────────────────
//
// Checks that a Verbose template written as a table row keeps its columns in
// place whatever the values, so that consecutive rows of the same event line
// up in the LogWindow and in a copied log and compare at a glance.
//
// A table row gives each pair a fixed width with the composite-format
// alignment, "{n,w}": numbers right-aligned so their units line up, words of a
// closed vocabulary left-aligned ("{n,-w}"). The check formats the template
// the way DispatchEventListener does (string.Format, invariant culture) once
// per sample row and compares where each " | " separator falls. The samples
// are the narrowest and widest values a field takes in practice: a value wider
// than its column still prints whole and shifts the row, it is never cut.
public static class TableTemplate
{
    public static string? Breach(string message, params object?[][] rows)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (rows.Length < 2) return "a table check needs at least two sample rows to compare";

        int[]? expected = null;
        string? expectedLine = null;
        foreach (object?[] row in rows)
        {
            string line = string.Format(CultureInfo.InvariantCulture, message, row);
            int[] separators = Separators(line);
            if (expected is null)
            {
                expected = separators;
                expectedLine = line;
                continue;
            }

            if (!separators.SequenceEqual(expected))
                return $"columns move between rows:\n  {expectedLine}\n  {line}";
        }
        return null;
    }

    private static int[] Separators(string line)
    {
        var positions = new List<int>();
        int index = 0;
        while ((index = line.IndexOf(" | ", index, StringComparison.Ordinal)) >= 0)
        {
            positions.Add(index);
            index += 3;
        }
        return positions.ToArray();
    }
}
