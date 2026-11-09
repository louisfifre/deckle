using System.Text.RegularExpressions;

namespace Deckle.Home;

// The comparison behind the near-name guard, and behind the write-path
// suggestions of HomeObjects.Resolve. Two names are near when any of these
// holds:
//   - their folded forms are equal (HomeNameGuard.Fold: case, accents,
//     ligatures, punctuation, spacing, a plural s or x);
//   - they hold the same words in any order once the French stop words are
//     dropped ("Lampe de bureau" ~ "bureau lampe");
//   - their keys (the significant words, sorted, joined by one space) are one
//     or two typos apart (optimal string alignment): at most 1 when the
//     shorter key has 5 to 10 characters, at most 2 above 10, none at 4 or
//     fewer, where one letter is a different word ("vis" and "vit").
// Names whose digit sequences differ ("Radiateur 1", "Radiateur 2") are
// never near, unless their folded forms are equal: a number tells twins apart.
internal static class HomeNearName
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "au", "aux", "d", "de", "des", "du", "en", "et", "l", "la", "le", "les",
        "pour", "sur", "un", "une",
    };

    private static readonly Regex Digits = new(
        "[0-9]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // What is compared, computed once per name.
    internal sealed record Profile(string Folded, string Key, string DigitSequence);

    public static Profile Describe(string? name)
    {
        string folded = HomeNameGuard.Fold(name);
        if (folded.Length == 0) return new Profile("", "", "");

        string[] significant = folded.Split(' ').Where(word => !StopWords.Contains(word)).ToArray();
        // A name made of stop words only keeps its whole folded form.
        string key = significant.Length == 0
            ? folded
            : string.Join(' ', significant.OrderBy(word => word, StringComparer.Ordinal));
        string digits = string.Join(',',
            Digits.Matches(folded).Select(match => match.Value).OrderBy(value => value, StringComparer.Ordinal));
        return new Profile(folded, key, digits);
    }

    public static bool AreNear(string? first, string? second) => AreNear(Describe(first), Describe(second));

    public static bool AreNear(Profile first, Profile second)
    {
        if (first.Folded.Length == 0 || second.Folded.Length == 0) return false;
        if (first.Folded == second.Folded) return true;
        if (first.DigitSequence != second.DigitSequence) return false;
        if (first.Key == second.Key) return true;

        int shorter = Math.Min(first.Key.Length, second.Key.Length);
        int tolerance = shorter <= 4 ? 0 : shorter <= 10 ? 1 : 2;
        return tolerance > 0 && Distance(first.Key, second.Key) <= tolerance;
    }

    // Optimal string alignment: insertion, deletion, substitution and the
    // swap of two neighbours each cost one.
    internal static int Distance(string first, string second)
    {
        int[,] table = new int[first.Length + 1, second.Length + 1];
        for (int row = 0; row <= first.Length; row++) table[row, 0] = row;
        for (int column = 0; column <= second.Length; column++) table[0, column] = column;
        for (int row = 1; row <= first.Length; row++)
            for (int column = 1; column <= second.Length; column++)
            {
                int cost = first[row - 1] == second[column - 1] ? 0 : 1;
                int best = Math.Min(
                    Math.Min(table[row - 1, column] + 1, table[row, column - 1] + 1),
                    table[row - 1, column - 1] + cost);
                if (row > 1 && column > 1
                    && first[row - 1] == second[column - 2] && first[row - 2] == second[column - 1])
                    best = Math.Min(best, table[row - 2, column - 2] + 1);
                table[row, column] = best;
            }
        return table[first.Length, second.Length];
    }
}