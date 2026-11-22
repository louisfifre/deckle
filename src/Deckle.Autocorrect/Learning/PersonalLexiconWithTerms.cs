namespace Deckle.Autocorrect;

// The personal dictionary plus the words the person declared elsewhere (the
// dictation lexicon). A declared word counts as adopted at once, with no
// recurrence to earn: it is shielded from correction and joins the candidates a
// typo can be corrected to. Only single words take part, since the correctors
// work one word at a time. The terms are read at each call, so an edit to the
// list applies without rebuilding the engine. Suppressions stay the
// dictionary's own.

/// <summary>An <see cref="IPersonalLexicon"/> that adds a live list of declared words to another one.</summary>
public sealed class PersonalLexiconWithTerms : IPersonalLexicon
{
    private readonly IPersonalLexicon _inner;
    private readonly Func<IEnumerable<string>> _terms;

    public PersonalLexiconWithTerms(IPersonalLexicon inner, Func<IEnumerable<string>> terms)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(terms);
        _inner = inner;
        _terms = terms;
    }

    public bool IsAdopted(string word)
    {
        if (_inner.IsAdopted(word)) return true;
        foreach (string term in Words())
        {
            if (string.Equals(term, word, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public bool IsSuppressed(string original, string replacement) => _inner.IsSuppressed(original, replacement);

    public IReadOnlyCollection<string> AdoptedWords
    {
        get
        {
            var words = new List<string>(_inner.AdoptedWords);
            var seen = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
            foreach (string term in Words())
            {
                if (seen.Add(term)) words.Add(term);
            }
            return words;
        }
    }

    private IEnumerable<string> Words()
    {
        IEnumerable<string>? terms;
        try
        {
            terms = _terms();
        }
        catch (Exception)
        {
            // The declared list is a bonus over the dictionary: unreadable, it is simply absent.
            yield break;
        }

        if (terms is null) yield break;
        foreach (string? raw in terms)
        {
            string term = raw?.Trim() ?? string.Empty;
            if (term.Length > 0 && !term.Any(char.IsWhiteSpace)) yield return term;
        }
    }
}
