using System.Text;
using Deckle.Input.Autocorrect;

namespace Deckle.Input.Autocorrect.Lab;

// Decides what is worth harvesting and what is noise or sensitive. Signal-only
// by design: only alphabetic, French-shaped tokens of word length pass, so
// digit-bearing tokens (PINs, card numbers, codes) and over-long tokens (keys,
// base64, concatenated secrets) are never persisted — a content backstop on top
// of the password-surface gate, and a quality filter (numbers and long blobs
// carry no correction signal). It does NOT catch a short all-letter passphrase
// typed into a field that fails to mark itself as a password (the residual
// keylogger class) — that stays an accepted, documented exposure of the tool.
// The richer plausibility cleanup (QWERTY adjacency, dictionary checks) is an
// offline read-side step on the harvest, not here.
public static class HarvestFilter
{
    // Real French words and the corrections worth keeping are short; a token
    // past this length is a key, an identifier, or a secret, not signal.
    public const int MaxTokenLength = 24;

    // A harvestable token: between two and MaxTokenLength characters, each a
    // letter or an in-word connector (apostrophe, hyphen), and bounded by
    // letters on both ends — so a trailing-apostrophe elision marker (l', qu')
    // or a stray leading/trailing connector is rejected, not mistaken for a word.
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

    // A correction pair worth keeping: both sides harvestable and actually
    // different.
    public static bool IsCorrectionPair(string original, string replacement) =>
        IsHarvestableToken(original) &&
        IsHarvestableToken(replacement) &&
        !string.Equals(original, replacement, StringComparison.Ordinal);

    // A committed word the French lexicon does not know. Classified on the
    // lexicon's own key space — lowercased + NFC — so the membership test never
    // misses on case or composition.
    public static bool IsUnknownWord(string word, FrequencyLexicon french)
    {
        if (!IsHarvestableToken(word))
            return false;

        string key = word.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        return !french.Contains(key);
    }
}
