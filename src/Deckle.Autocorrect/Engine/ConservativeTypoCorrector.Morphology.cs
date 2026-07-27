namespace Deckle.Autocorrect;

public sealed partial class ConservativeTypoCorrector
{
    private CorrectionDecision? EvaluateMorphologicalAccent(
        string word,
        string lower,
        bool literalValid,
        IReadOnlyList<string> leftContext,
        StageTrace? st)
    {
        if (_accentIndex is null)
            return null;

        IReadOnlyList<AccentVariant> accents = _accentIndex.VariantsOf(lower);
        string previous = leftContext.Count > 0
            ? GrammarCorrector.ContextTail(leftContext[^1])
            : string.Empty;

        if (_verbs is not null && accents.Count > 0)
        {
            if (GrammarCorrector.TryGetRequiredPerson(leftContext, out string person))
            {
                CorrectionDecision? finite = UniqueMorphologicalCandidate(
                    word, accents,
                    candidate => _verbs.HasUnambiguousFiniteReading(candidate, person),
                    st, CorrectionTrace.Reasons.TypoSubjectAgreement);
                if (finite is not null)
                    return finite;
            }

            if (AvoirAuxiliaries.Contains(previous))
            {
                CorrectionDecision? participle = UniqueMorphologicalCandidate(
                    word, accents, _verbs.HasPastParticipleReading,
                    st, CorrectionTrace.Reasons.AuxiliaryParticiple);
                if (participle is not null)
                    return participle;
            }
        }

        // A determiner makes the most frequent direct accent restoration much
        // stronger than a physical edit. Keep a 5x margin between folded rivals.
        if (!literalValid && Determiners.Contains(previous) && accents.Count > 0
            && (accents.Count == 1
                || accents[0].FrequencyPerMillion
                    >= accents[1].FrequencyPerMillion * _options.DominanceRatio))
        {
            st?.WithCandidates(accents, _ => CorrectionTrace.Sources.Index)
              .Fire(CorrectionTrace.Reasons.DeterminerAccent);
            return new CorrectionDecision(
                word, CasePattern.Apply(word, accents[0].Form), CorrectionReason.LexicalGate);
        }

        // Some source rows omit a regular plural while retaining the accented
        // singular. Synthesize only a unique non-verb singular; this reaches
        // hébergements without turning verb participles such as proposées into
        // an unrestricted plural rule.
        if (!literalValid && _verbs is not null && accents.Count == 0
            && lower.Length > 3 && lower.EndsWith('s'))
        {
            IReadOnlyList<AccentVariant> singulars = _accentIndex.VariantsOf(lower[..^1]);
            AccentVariant? noun = null;
            foreach (AccentVariant singular in singulars)
            {
                if (_verbs.IsVerb(singular.Form))
                    continue;
                if (noun is not null)
                    return null;
                noun = singular;
            }
            if (noun is AccentVariant singularNoun)
            {
                string plural = singularNoun.Form + "s";
                st?.AddCandidate(plural, singularNoun.FrequencyPerMillion, CorrectionTrace.Sources.Index)
                  .Fire(CorrectionTrace.Reasons.RegularPluralAccent);
                return new CorrectionDecision(
                    word, CasePattern.Apply(word, plural), CorrectionReason.LexicalGate);
            }
        }

        return null;
    }

    private static CorrectionDecision? UniqueMorphologicalCandidate(
        string word,
        IReadOnlyList<AccentVariant> candidates,
        Func<string, bool> matches,
        StageTrace? st,
        string reason)
    {
        AccentVariant? match = null;
        foreach (AccentVariant candidate in candidates)
        {
            if (!matches(candidate.Form))
                continue;
            if (match is not null)
                return null;
            match = candidate;
        }
        if (match is not AccentVariant chosen)
            return null;

        st?.WithCandidates(candidates, _ => CorrectionTrace.Sources.Index).Fire(reason);
        return new CorrectionDecision(
            word, CasePattern.Apply(word, chosen.Form), CorrectionReason.LexicalGate);
    }

    // Avoir is a strong compound-past marker. Être is deliberately absent:
    // after the copula an adjective/adverb typo remains plausible (c'est bine ->
    // c'est bien), so a participle-only shortcut would be unsafe.
    private static readonly HashSet<string> AvoirAuxiliaries = new(StringComparer.Ordinal)
    {
        "ai", "as", "a", "avons", "avez", "ont",
    };

    private static readonly HashSet<string> Determiners = new(StringComparer.Ordinal)
    {
        "un", "une", "le", "la", "les", "du", "des",
    };
}

// Priority view over the same lexical and keyboard evidence. It acts only on a
// closed morphology proof: subject agreement, avoir + participle, a determined
// accent fold, or a regular non-verb plural.
public sealed class MorphologyCorrector : ICorrectionPolicy
{
    private readonly ConservativeTypoCorrector _inner;

    public MorphologyCorrector(ConservativeTypoCorrector inner) => _inner = inner;

    public CorrectionDecision? Evaluate(
        string word,
        IReadOnlyList<string> leftContext,
        CorrectionTrace? trace = null) =>
        _inner.EvaluateMorphology(word, leftContext, trace);
}
