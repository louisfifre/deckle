namespace Deckle.Autocorrect;

// Deterministic French sentence rules in front of the optional MLM reranker.
// The rules only choose from the closed candidate set the probe already built;
// when none applies, the optional inner reranker gets the slot unchanged.
public sealed class FrenchSentenceReranker : ISentenceReranker, IWholeSentenceReranker, IDisposable
{
    private readonly ISentenceReranker? _inner;

    private static readonly HashSet<string> EtreForms = new(StringComparer.Ordinal)
    {
        "suis", "es", "est", "sommes", "êtes", "etes", "sont",
        "étais", "etais", "était", "etait", "étions", "etions", "étiez", "etiez", "étaient", "etaient",
        "fus", "fut", "fûmes", "fumes", "fûtes", "futes", "furent",
        "serai", "seras", "sera", "serons", "serez", "seront",
        "serais", "serait", "serions", "seriez", "seraient",
        "sois", "soit", "soyons", "soyez", "soient",
    };

    private static readonly HashSet<string> LocativeBridgeWords = new(StringComparer.Ordinal)
    {
        "déjà", "deja",
        "encore",
        "toujours",
        "maintenant",
    };

    public FrenchSentenceReranker(ISentenceReranker? inner = null)
    {
        _inner = inner;
    }

    public RerankOutcome Rerank(
        IReadOnlyList<string> sentence,
        int slotIndex,
        IReadOnlyList<AccentVariant> candidates)
    {
        RerankOutcome? rule = TryLocativeLa(sentence, slotIndex, candidates);
        if (rule is not null)
            return rule.Value;

        return _inner?.Rerank(sentence, slotIndex, candidates)
            ?? RerankOutcome.Abstained(RerankOutcome.AbstainReasons.NoRule);
    }

    public void Dispose() => (_inner as IDisposable)?.Dispose();

    public RerankOutcome RerankSentence(
        IReadOnlyList<string> sentence,
        IReadOnlyList<SentenceEditCandidate> candidates)
    {
        // Preserve the short-context locative rule without reopening independent
        // slot judgments: when its exact one-edit sentence is in the global set,
        // it returns one global verdict.
        foreach (SentenceEditCandidate candidate in candidates)
        {
            if (!string.Equals(candidate.Form, "là", StringComparison.Ordinal)
                || candidate.SlotIndex <= 0
                || candidate.SlotIndex >= sentence.Count
                || !string.Equals(sentence[candidate.SlotIndex], "la", StringComparison.Ordinal))
                continue;

            AccentVariant[] pair =
            [
                new("la", 0.0),
                new("là", 0.0),
            ];
            RerankOutcome? rule = TryLocativeLa(sentence, candidate.SlotIndex, pair);
            if (rule is not RerankOutcome chosen)
                continue;

            return chosen with { ChosenSlotIndex = candidate.SlotIndex };
        }

        return _inner is IWholeSentenceReranker wholeSentence
            ? wholeSentence.RerankSentence(sentence, candidates)
            : RerankOutcome.Abstained(
                RerankOutcome.AbstainReasons.WholeSentenceUnsupported);
    }

    private static RerankOutcome? TryLocativeLa(
        IReadOnlyList<string> sentence,
        int slotIndex,
        IReadOnlyList<AccentVariant> candidates)
    {
        if (slotIndex <= 0 || slotIndex >= sentence.Count)
            return null;
        if (!string.Equals(sentence[slotIndex], "la", StringComparison.Ordinal))
            return null;

        // Conservative scope: only "etre + la" at the end of the submitted group.
        // "je suis la personne" must stay article because it has right context.
        if (slotIndex != sentence.Count - 1)
            return null;

        if (!HasEtreAnchor(sentence, slotIndex))
            return null;

        bool hasLa = false, hasLaAccent = false;
        foreach (AccentVariant c in candidates)
        {
            hasLa |= string.Equals(c.Form, "la", StringComparison.Ordinal);
            hasLaAccent |= string.Equals(c.Form, "là", StringComparison.Ordinal);
        }
        if (!hasLa || !hasLaAccent)
            return null;

        return new RerankOutcome(
            "là",
            Scores(candidates, chosen: "là"),
            Margin: 1.0,
            Threshold: 0.0,
            AbstainReason: null);
    }

    private static bool HasEtreAnchor(IReadOnlyList<string> sentence, int slotIndex)
    {
        string previous = sentence[slotIndex - 1].ToLowerInvariant();
        if (EtreForms.Contains(previous))
            return true;

        if (!LocativeBridgeWords.Contains(previous) || slotIndex < 2)
            return false;

        string anchor = sentence[slotIndex - 2].ToLowerInvariant();
        return EtreForms.Contains(anchor);
    }

    private static IReadOnlyList<RerankCandidateScore> Scores(
        IReadOnlyList<AccentVariant> candidates,
        string chosen)
    {
        var scores = new RerankCandidateScore[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
            scores[i] = new RerankCandidateScore(
                candidates[i].Form,
                string.Equals(candidates[i].Form, chosen, StringComparison.Ordinal) ? 1.0 : 0.0);
        return scores;
    }
}
