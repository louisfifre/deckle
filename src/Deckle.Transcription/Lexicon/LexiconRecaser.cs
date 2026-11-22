namespace Deckle.Transcription.Lexicon;

// Gives the words of the lexicon their exact casing in a transcript: "deckle"
// becomes "Deckle", "winui" becomes "WinUI". The pass changes letter case only,
// never a letter itself, so it cannot turn one word into another; that is why it
// runs on every take, while the decoder boost has its own switch.
//
// Only terms with a capital letter take part. A term written all in lowercase
// says nothing about case, and applying it would lowercase that word at the
// start of a sentence.

/// <summary>Restores the casing of lexicon terms in a text.</summary>
public sealed class LexiconRecaser
{
    private readonly string[] _terms;

    public LexiconRecaser(IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        // Longest first, so a phrase ("Ada Lovelace") is fixed as a whole before its parts are.
        _terms = terms
            .Where(LexiconTerms.HasCapital)
            .OrderByDescending(t => t.Length)
            .ToArray();
    }

    /// <summary>True when no term can change anything.</summary>
    public bool IsEmpty => _terms.Length == 0;

    /// <summary>The text with each whole-word occurrence of a term, in any case, written as the term.</summary>
    public string Apply(string text)
    {
        if (_terms.Length == 0 || string.IsNullOrEmpty(text)) return text;

        char[]? buffer = null;
        foreach (string term in _terms)
        {
            ReadOnlySpan<char> current = buffer ?? text.AsSpan();
            int from = 0;
            while (from <= current.Length - term.Length)
            {
                int index = current[from..].IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                index += from;

                if (IsWordBoundary(current, index - 1) && IsWordBoundary(current, index + term.Length)
                    && !current.Slice(index, term.Length).SequenceEqual(term))
                {
                    buffer ??= text.ToCharArray();
                    term.AsSpan().CopyTo(buffer.AsSpan(index));
                    current = buffer;
                }

                from = index + term.Length;
            }
        }

        return buffer is null ? text : new string(buffer);
    }

    // Outside the text, or a character that cannot continue a word. An apostrophe
    // counts as a boundary so "l'deckle" still finds "deckle"; a hyphen does too.
    private static bool IsWordBoundary(ReadOnlySpan<char> text, int index) =>
        index < 0 || index >= text.Length || !char.IsLetterOrDigit(text[index]);
}
