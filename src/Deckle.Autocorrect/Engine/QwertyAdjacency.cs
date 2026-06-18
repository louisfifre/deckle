namespace Deckle.Autocorrect;

// Physically touching keys on a QWERTY-US keyboard — the layout Louis dictates
// French on. A wrong-key slip lands on a neighbour, so the typo corrector only
// treats a substitution as plausible when the two letters actually touch
// (horizontally, vertically or diagonally). Deletions, insertions and
// transpositions need no layout — a missing or doubled key is layout-agnostic.
internal static class QwertyAdjacency
{
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
}
