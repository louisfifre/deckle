using System.Text;

namespace Deckle.Transcription.Lexicon;

// The one list every consumer of the lexicon reads: the person's entries,
// cleaned. Two entries that differ only by case are one word, and the first
// keeps its spelling.

/// <summary>Cleans and merges lexicon entries into one list of distinct terms.</summary>
public static class LexiconTerms
{
    /// <summary>A term longer than this is a sentence, not a word: it is dropped.</summary>
    public const int MaxTermLength = 64;

    /// <summary>
    /// A one-letter term is dropped: the casing pass would rewrite every occurrence of that
    /// letter (« A » would turn each French « a » into « A »), and the decoder boost would
    /// push it on every word start.
    /// </summary>
    public const int MinTermLength = 2;

    /// <summary>
    /// Trims each entry, collapses inner whitespace to one space, drops one-letter and over-long
    /// entries, and keeps the first spelling of each case-insensitive duplicate, in input order.
    /// </summary>
    public static IReadOnlyList<string> Merge(params IEnumerable<string>?[] sources)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var terms = new List<string>();
        foreach (IEnumerable<string>? source in sources)
        {
            if (source is null) continue;
            foreach (string? raw in source)
            {
                string term = Clean(raw);
                if (term.Length < MinTermLength || term.Length > MaxTermLength) continue;
                if (seen.Add(term)) terms.Add(term);
            }
        }

        return terms;
    }

    /// <summary>The entry as stored: NFC, trimmed, one space between words.</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var builder = new StringBuilder(raw.Length);
        bool pendingSpace = false;
        foreach (char c in raw.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace) builder.Append(' ');
            pendingSpace = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>True when the term carries a capital letter, so its casing is information worth restoring.</summary>
    public static bool HasCapital(string term)
    {
        foreach (char c in term)
        {
            if (char.IsUpper(c)) return true;
        }

        return false;
    }
}
