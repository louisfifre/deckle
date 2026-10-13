using System.Text;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// Decides what is worth harvesting and what is noise or sensitive. Only
// alphabetic tokens of word length pass, so digit-bearing tokens (PINs, card
// numbers, codes) and over-long tokens (keys, base64, concatenated secrets) are
// never persisted: a content backstop on top of the password-surface gate, and a
// quality filter. It does NOT catch a short all-letter passphrase typed into a
// field that fails to mark itself as a password; that exposure is accepted.
public static class HarvestFilter
{
    // Real French words and the corrections worth keeping are short; a token
    // past this length is a key, an identifier, or a secret, not signal.
    public const int MaxTokenLength = 24;

    // Bounded by letters on both ends, so an elision marker (l', qu') or a stray
    // leading or trailing connector is rejected, not mistaken for a word.
    public static bool IsHarvestableToken(string word)
    {
        if (word.Length < 2 || word.Length > MaxTokenLength)
            return false;

        if (!char.IsLetter(word[0]) || !char.IsLetter(word[^1]))
            return false;

        foreach (char c in word)
            if (!char.IsLetter(c) && c != '\'' && c != '-')
                return false;

        return true;
    }

    public static bool IsCorrectionPair(string original, string replacement) =>
        IsHarvestableToken(original) &&
        IsHarvestableToken(replacement) &&
        !string.Equals(original, replacement, StringComparison.Ordinal);

    // Classified on the lexicon's own key space (lowercased + NFC), so the
    // membership test never misses on case or composition.
    public static bool IsUnknownWord(string word, FrequencyLexicon french)
    {
        if (!IsHarvestableToken(word))
            return false;

        string key = word.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        return !french.Contains(key);
    }
}
