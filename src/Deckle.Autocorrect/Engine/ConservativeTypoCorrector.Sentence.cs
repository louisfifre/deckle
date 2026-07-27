namespace Deckle.Autocorrect;

public sealed partial class ConservativeTypoCorrector
{
    // The commit stage abstains when several plausible neighbours fail its
    // dominance bar. Expose a bounded closed set plus the literal as KEEP to the
    // full-sentence judge without relaxing the instant path.
    public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
        SentenceCandidates(word, includeTypedLiteral: true);

    public IReadOnlyList<AccentVariant> SentenceCandidates(
        string word,
        bool includeTypedLiteral)
    {
        if (!CanProbeSentence(word, out string lower))
            return Array.Empty<AccentVariant>();

        List<Candidate> near = ValidNeighbours(
            lower, twoEdits: false, includeCoherentHorizontalShifts: true);
        List<Candidate>? far = null;

        // Short hurried tokens often combine two physical slips (miru -> mieux).
        // The sentence judge can reach them safely with KEEP in the candidate set.
        if (_options.MaxEditDistance >= 2 && lower.Length <= ContextualFarMaxWordLength)
        {
            var nearForms = new HashSet<string>(
                near.Select(candidate => candidate.Form), StringComparer.Ordinal);
            far = ValidNeighbours(lower, twoEdits: true)
                .Where(candidate => !nearForms.Contains(candidate.Form))
                .ToList();
        }

        var candidates = new List<AccentVariant>(SentenceCandidateCap + 1);
        AddCandidates(near);
        if (candidates.Count < SentenceCandidateCap && far is not null)
            AddCandidates(far);

        void AddCandidates(IEnumerable<Candidate> neighbours)
        {
            foreach (Candidate neighbour in neighbours)
            {
                if (neighbour.Frequency < _options.MinFrequencyPerMillion)
                    continue;
                if (_personal?.IsSuppressed(lower, neighbour.Form) == true)
                    continue;
                candidates.Add(new AccentVariant(neighbour.Form, neighbour.Frequency));
                if (candidates.Count == SentenceCandidateCap)
                    return;
            }
        }

        if (candidates.Count == 0)
            return Array.Empty<AccentVariant>();
        candidates.Add(new AccentVariant(lower, 0.0));
        return candidates;
    }

    private bool CanProbeSentence(string word, out string lower)
    {
        lower = string.Empty;
        if (word.Length < _options.MinWordLength
            || WordShape.HasInternalUpper(word)
            || WordShape.IsTitleCase(word)
            || AccentFolding.HasDiacritics(word))
            return false;
        foreach (char c in word)
            if (!char.IsLetter(c))
                return false;

        lower = word.ToLowerInvariant();
        return !_french.Contains(lower)
            && _english?.Contains(lower) != true
            && _personal?.IsAdopted(lower) != true;
    }
}
