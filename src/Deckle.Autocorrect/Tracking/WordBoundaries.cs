using System.Globalization;
using System.Text;

namespace Deckle.Autocorrect;

// The ONE canonical French tokenization, shared by the live TypedWordTracker
// and the future offline trainer/eval so a word is split the same way wherever
// it is seen. The rules it owns:
//   • a word is letters (any script), digits, and the hyphen-minus — so
//     « est-ce » and « rendez-vous » stay single tokens;
//   • an apostrophe after a known elision prefix CLOSES the token with the
//     apostrophe attached (« l' », « jusqu' »), the rest starts fresh; any
//     other apostrophe joins the token (« aujourd'hui » is one token);
//   • both apostrophe glyphs (U+0027, U+2019) are normalized to U+0027 in
//     emitted tokens, so downstream lookups have a single canonical form.
public static class WordBoundaries
{
    private const char Apostrophe = '\'';        // U+0027 — the canonical form
    private const char RightSingleQuote = '’';

    // Elision prefixes that legitimately end on an apostrophe in French.
    // Stored accent-free and lowercase; candidates are folded the same way.
    private static readonly HashSet<string> ElisionPrefixes = new(StringComparer.Ordinal)
    {
        "l", "d", "j", "n", "m", "t", "s", "c",
        "qu", "jusqu", "lorsqu", "puisqu", "quoiqu",
    };

    public static bool IsWordChar(char c) =>
        char.IsLetter(c) || char.IsDigit(c) || c == '-';

    public static bool IsApostrophe(char c) =>
        c == Apostrophe || c == RightSingleQuote;

    public static bool IsElisionPrefix(string token) =>
        ElisionPrefixes.Contains(Fold(token));

    public static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();

        foreach (char c in text)
        {
            if (IsWordChar(c))
            {
                sb.Append(c);
                continue;
            }

            if (IsApostrophe(c))
            {
                if (sb.Length == 0)
                    continue; // leading apostrophe is a plain separator

                if (IsElisionPrefix(sb.ToString()))
                {
                    sb.Append(Apostrophe); // attach the normalized apostrophe and close
                    yield return sb.ToString();
                    sb.Clear();
                }
                else
                {
                    sb.Append(Apostrophe); // joins the current token (« aujourd'hui »)
                }
                continue;
            }

            // Any other non-word char ends the token (without the terminator).
            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    // Lowercase + strip diacritics, for accent-insensitive prefix matching.
    private static string Fold(string s)
    {
        string lowered = s.ToLowerInvariant();
        string decomposed = lowered.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }
}
