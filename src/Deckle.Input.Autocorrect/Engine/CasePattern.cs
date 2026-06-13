using System.Globalization;

namespace Deckle.Input.Autocorrect.Engine;

// ── CasePattern ─────────────────────────────────────────────────────────────
//
// Candidates are stored lowercase, but the user typed a case the correction
// must honour: "Ecole" → "École", "FRANCAIS" → "FRANÇAIS". This transfers the
// typed word's case shape onto the replacement.
//
// Three recognised shapes, everything else passes through unchanged (the
// conservative default — an irregular shape is left to the lowercase form):
//   all-lower            → replacement as-is
//   first-upper-rest-low → capitalise the replacement's first letter
//   all-upper (len > 1)  → uppercase the whole replacement
public static class CasePattern
{
    public static string Apply(string typed, string replacement)
    {
        if (typed.Length == 0 || replacement.Length == 0)
            return replacement;

        bool firstUpper = char.IsUpper(typed[0]);

        // Title shape: a single uppercase char, or first-upper with no other
        // uppercase. Capitalise the replacement's head (É must survive, hence
        // ToUpperInvariant on a possibly-accented letter).
        if (firstUpper && (typed.Length == 1 || !HasUpperAfter(typed, 1)))
        {
            return string.Concat(
                replacement[0].ToString().ToUpperInvariant(),
                replacement.AsSpan(1));
        }

        // All-upper shout, more than one char.
        if (typed.Length > 1 && IsAllUpper(typed))
            return replacement.ToUpperInvariant();

        // all-lower and any unrecognised shape (camel, mixed) → as-is.
        return replacement;
    }

    private static bool HasUpperAfter(string s, int start)
    {
        for (int i = start; i < s.Length; i++)
            if (char.IsUpper(s[i]))
                return true;
        return false;
    }

    // All-upper = no lowercase letter anywhere (cased chars only count; a
    // hyphen or apostrophe is neutral).
    private static bool IsAllUpper(string s)
    {
        foreach (char c in s)
            if (char.IsLower(c))
                return false;
        return true;
    }
}
