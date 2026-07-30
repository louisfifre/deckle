namespace Deckle.Autocorrect;

// Supplies one surface-only gender alternative to the whole-sentence judge:
// terminal e added or removed, only when both endpoints are valid primary-
// language words. It never enters the legacy slot-local path. The literal and
// every one-edit sentence compete together, so un/une and seul/seule cannot be
// decided as independent cascading repairs.
public sealed class GenderVariantProbe : IAmbiguityProbe
{
    private readonly IFrequencyLexicon _french;
    private readonly IFrequencyLexicon? _english;
    private readonly IPersonalLexicon? _personal;

    public GenderVariantProbe(
        IFrequencyLexicon french,
        IFrequencyLexicon? english = null,
        IPersonalLexicon? personal = null)
    {
        _french = french;
        _english = english;
        _personal = personal;
    }

    public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
        SentenceCandidates(word, includeTypedLiteral: true);

    public IReadOnlyList<AccentVariant> SentenceCandidates(
        string word,
        bool includeTypedLiteral)
    {
        if (!CanProbe(word, out string lower))
            return Array.Empty<AccentVariant>();

        string alternative = lower.EndsWith('e')
            ? lower[..^1]
            : lower + "e";
        if (alternative.Length < 2
            || !_french.Contains(alternative)
            || _personal?.IsSuppressed(lower, alternative) == true)
            return Array.Empty<AccentVariant>();

        var candidates = new List<AccentVariant>(2)
        {
            new(alternative, _french.FrequencyOf(alternative)),
        };
        if (includeTypedLiteral)
            candidates.Add(new AccentVariant(lower, _french.FrequencyOf(lower)));
        return candidates;
    }

    private bool CanProbe(string word, out string lower)
    {
        lower = string.Empty;
        if (word.Length < 2
            || WordShape.HasInternalUpper(word)
            || WordShape.IsTitleCase(word)
            || AccentFolding.HasDiacritics(word))
            return false;

        foreach (char character in word)
            if (!char.IsLetter(character))
                return false;

        lower = word.ToLowerInvariant();
        return _french.Contains(lower)
            && _english?.Contains(lower) != true
            && _personal?.IsAdopted(lower) != true;
    }
}
