namespace Deckle.Autocorrect;

// Cheap structural predicates on a raw-case token, shared by the correction
// policies — no lexicon, no allocation. camelCase identifiers and mid-utterance
// proper nouns are shapes both the diacritics gate and the typo corrector must
// leave alone, so the tests live once here.
internal static class WordShape
{
    // True for camelCase/PascalCase identifiers: an uppercase past index 0 on a
    // word that is not entirely uppercase (fooBar, camelCase). All-upper
    // acronyms have no internal *lower*, so they are not identifiers.
    public static bool HasInternalUpper(string word)
    {
        bool anyLower = false;
        foreach (char c in word)
            if (char.IsLower(c)) { anyLower = true; break; }

        if (!anyLower)
            return false; // all-upper (or no cased letters) — not an identifier.

        for (int i = 1; i < word.Length; i++)
            if (char.IsUpper(word[i]))
                return true;
        return false;
    }

    // True for a plain title-cased token: a leading capital, a lowercase tail,
    // and no internal capital ("Git", "Azure"). All-caps acronyms and camelCase
    // (internal capital) are deliberately excluded — and so are hyphenated names
    // carrying an internal capital ("États-Unis"), which stay eligible.
    public static bool IsTitleCase(string word)
    {
        if (word.Length < 2 || !char.IsUpper(word[0]))
            return false;
        bool anyLower = false;
        for (int i = 1; i < word.Length; i++)
        {
            if (char.IsUpper(word[i]))
                return false;
            if (char.IsLower(word[i]))
                anyLower = true;
        }
        return anyLower;
    }
}
