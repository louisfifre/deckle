namespace Deckle.Autocorrect;

// Physically touching keys on a QWERTY-US keyboard — the layout Louis dictates
// French on. A wrong-key slip lands on a neighbour, so the typo corrector only
// treats a substitution as plausible when the two letters actually touch
// (horizontally, vertically or diagonally). Deletions, insertions and
// transpositions need no layout — a missing or doubled key is layout-agnostic.
internal static class QwertyAdjacency
{
    private static readonly string[] Rows = ["qwertyuiop", "asdfghjkl", "zxcvbnm"];

    // Lowercase letter → the letters whose keys physically touch it. Symmetric
    // by construction; only the 26 letter keys are modelled (no digits row, no
    // punctuation — those tokens never reach the corrector).
    private static readonly Dictionary<char, string> Neighbourhood = new()
    {
        ['q'] = "was",
        ['w'] = "qeasd",
        ['e'] = "wrsdf",
        ['r'] = "etdfg",
        ['t'] = "ryfgh",
        ['y'] = "tughj",
        ['u'] = "yihjk",
        ['i'] = "uojkl",
        ['o'] = "ipkl",
        ['p'] = "ol",
        ['a'] = "qwsz",
        ['s'] = "qweadzx",
        ['d'] = "wersfxc",
        ['f'] = "ertdgcv",
        ['g'] = "rtyfhvb",
        ['h'] = "tyugjbn",
        ['j'] = "yuihknm",
        ['k'] = "uiojlm",
        ['l'] = "iopk",
        ['z'] = "asx",
        ['x'] = "sdzc",
        ['c'] = "dfxv",
        ['v'] = "fgcb",
        ['b'] = "ghvn",
        ['n'] = "hjbm",
        ['m'] = "jkn",
    };

    // The touching keys of a lowercase letter, or an empty span for anything
    // outside the modelled letter set.
    public static string Neighbours(char lower) =>
        Neighbourhood.TryGetValue(lower, out string? n) ? n : string.Empty;

    // A hand displaced one key horizontally produces a coherent run rather than
    // independent random substitutions ("ui" → "io", hence "qui" → "qio").
    // Yield every string obtained by shifting a contiguous run of at least two
    // letters one key left or right. This is contextual-candidate material only:
    // the instant typo gate never silently applies it without sentence evidence.
    public static IEnumerable<string> CoherentHorizontalShifts(string word)
    {
        for (int start = 0; start < word.Length - 1; start++)
        {
            for (int length = 2; start + length <= word.Length; length++)
            {
                for (int direction = -1; direction <= 1; direction += 2)
                {
                    char[] shifted = word.ToCharArray();
                    bool valid = true;
                    for (int index = start; index < start + length; index++)
                    {
                        if (!TryShift(word[index], direction, out shifted[index]))
                        {
                            valid = false;
                            break;
                        }
                    }
                    if (valid)
                        yield return new string(shifted);
                }
            }
        }
    }

    private static bool TryShift(char letter, int direction, out char shifted)
    {
        foreach (string row in Rows)
        {
            int index = row.IndexOf(letter);
            if (index < 0) continue;
            int target = index + direction;
            if (target >= 0 && target < row.Length)
            {
                shifted = row[target];
                return true;
            }
            break;
        }
        shifted = default;
        return false;
    }
}
