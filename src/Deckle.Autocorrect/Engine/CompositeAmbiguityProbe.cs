namespace Deckle.Autocorrect;

// One sentence-stage candidate surface composed from disjoint commit policies.
// Diacritics and keyboard typos can both leave a literal deliberately unresolved;
// their closed candidate sets meet here without coupling either policy to the
// coordinator or to a model implementation.
public sealed class CompositeAmbiguityProbe : IAmbiguityProbe
{
    private readonly IReadOnlyList<IAmbiguityProbe> _probes;

    public CompositeAmbiguityProbe(params IAmbiguityProbe[] probes) =>
        _probes = probes ?? Array.Empty<IAmbiguityProbe>();

    public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
        Merge(probe => probe.AmbiguousCandidates(word));

    public IReadOnlyList<AccentVariant> SentenceCandidates(
        string word,
        bool includeTypedLiteral) =>
        Merge(probe => probe.SentenceCandidates(word, includeTypedLiteral));

    private IReadOnlyList<AccentVariant> Merge(
        Func<IAmbiguityProbe, IReadOnlyList<AccentVariant>> select)
    {
        var byForm = new Dictionary<string, AccentVariant>(StringComparer.Ordinal);
        foreach (IAmbiguityProbe probe in _probes)
        {
            foreach (AccentVariant candidate in select(probe))
            {
                if (!byForm.TryGetValue(candidate.Form, out AccentVariant prior)
                    || candidate.FrequencyPerMillion > prior.FrequencyPerMillion)
                {
                    byForm[candidate.Form] = candidate;
                }
            }
        }

        if (byForm.Count < 2)
            return Array.Empty<AccentVariant>();
        return byForm.Values
            .OrderByDescending(candidate => candidate.FrequencyPerMillion)
            .ToArray();
    }
}
