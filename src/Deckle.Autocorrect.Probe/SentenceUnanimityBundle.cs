using System.Collections.Frozen;
using System.Collections.Immutable;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceUnanimityBundle
{
    public const string RuleDisagreement = "rule_disagreement";

    private static readonly FrenchSentenceReranker LocativeReranker = new();

    private static readonly FrozenSet<string> AvoirForms = new[]
    {
        "ai", "as", "a", "avons", "avez", "ont",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ModalForms = new[]
    {
        "vais", "vas", "va", "allons", "allez", "vont",
        "peux", "peut", "pouvons", "pouvez", "peuvent",
        "dois", "doit", "devons", "devez", "doivent", "faut",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static FrozenSet<string> FrozenRuleIds { get; } = new[]
    {
        SentenceUnanimityRuleIds.ExistingTerminalLocativeLa,
        SentenceUnanimityRuleIds.AvoirPastParticiple,
        SentenceUnanimityRuleIds.ModalInfinitive,
        SentenceUnanimityRuleIds.AvoirDuBeforeInfinitive,
        SentenceUnanimityRuleIds.RestrictiveNYAQuaBeforeInfinitive,
    }.ToFrozenSet(StringComparer.Ordinal);

    public static ImmutableArray<string> FrozenRuleOrder { get; } =
        ImmutableArray.Create(
        SentenceUnanimityRuleIds.ExistingTerminalLocativeLa,
        SentenceUnanimityRuleIds.AvoirPastParticiple,
        SentenceUnanimityRuleIds.ModalInfinitive,
        SentenceUnanimityRuleIds.AvoirDuBeforeInfinitive,
        SentenceUnanimityRuleIds.RestrictiveNYAQuaBeforeInfinitive);

    public static DecisionVerdict Evaluate(
        DecisionEvaluationScenario scenario,
        IReadOnlyList<string> ruleOrder,
        VerbMorphology morphology) =>
        Resolve(scenario, CollectClaims(scenario, ruleOrder, morphology));

    internal static IReadOnlyList<SentenceRuleClaim> CollectClaims(
        DecisionEvaluationScenario scenario,
        IReadOnlyList<string> ruleOrder,
        VerbMorphology morphology)
    {
        ValidateRuleOrder(ruleOrder);
        var claims = new List<SentenceRuleClaim>(ruleOrder.Count);
        foreach (string ruleId in ruleOrder)
        {
            string? candidateIdentity = EvaluateRule(ruleId, scenario, morphology);
            if (candidateIdentity is not null)
                claims.Add(new SentenceRuleClaim(ruleId, candidateIdentity));
        }
        return claims;
    }

    internal static DecisionVerdict Resolve(
        DecisionEvaluationScenario scenario,
        IReadOnlyList<SentenceRuleClaim> claims)
    {
        if (claims.Count == 0)
        {
            return new DecisionVerdict(
                DecisionVerdictKind.Abstain,
                CandidateIdentity: null,
                FinalText: null,
                RerankOutcome.AbstainReasons.NoRule);
        }

        string candidateIdentity = claims[0].CandidateIdentity;
        if (claims.Any(claim => !string.Equals(
                claim.CandidateIdentity,
                candidateIdentity,
                StringComparison.Ordinal)))
        {
            return new DecisionVerdict(
                DecisionVerdictKind.Abstain,
                CandidateIdentity: null,
                FinalText: null,
                RuleDisagreement);
        }

        DecisionCandidate[] matches = scenario.Candidates
            .Where(candidate => string.Equals(
                candidate.Identity,
                candidateIdentity,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "A unanimous rule claim did not identify one submitted edit.");

        DecisionCandidate chosen = matches[0];
        return new DecisionVerdict(
            DecisionVerdictKind.Edit,
            chosen.Identity,
            SentenceDecisionInventory.Apply(scenario.Transaction.Literal, chosen),
            AbstainReason: null);
    }

    private static string? EvaluateRule(
        string ruleId,
        DecisionEvaluationScenario scenario,
        VerbMorphology morphology) => ruleId switch
    {
        SentenceUnanimityRuleIds.ExistingTerminalLocativeLa =>
            TryExistingTerminalLocativeLa(scenario),
        SentenceUnanimityRuleIds.AvoirPastParticiple =>
            TryAvoirPastParticiple(scenario, morphology),
        SentenceUnanimityRuleIds.ModalInfinitive =>
            TryModalInfinitive(scenario, morphology),
        SentenceUnanimityRuleIds.AvoirDuBeforeInfinitive =>
            TryAvoirDuBeforeInfinitive(scenario, morphology),
        SentenceUnanimityRuleIds.RestrictiveNYAQuaBeforeInfinitive =>
            TryRestrictiveNYAQuaBeforeInfinitive(scenario, morphology),
        _ => throw new ArgumentOutOfRangeException(
            nameof(ruleId),
            ruleId,
            "The rule identifier is not in the frozen bundle."),
    };

    private static string? TryExistingTerminalLocativeLa(
        DecisionEvaluationScenario scenario)
    {
        DecisionVerdict verdict = SentenceDecisionInventoryEvaluation.Classify(
            LocativeReranker.RerankSentence(scenario.Transaction),
            scenario);
        return verdict.Kind == DecisionVerdictKind.Edit
            ? verdict.CandidateIdentity
            : null;
    }

    private static string? TryAvoirPastParticiple(
        DecisionEvaluationScenario scenario,
        VerbMorphology morphology) =>
        TryAnchoredErParadigm(
            scenario,
            HasStrongAvoirPastParticipleAnchor,
            desiredEnding: "é",
            morphology,
            desiredMode: "par",
            desiredTense: "pas");

    private static string? TryModalInfinitive(
        DecisionEvaluationScenario scenario,
        VerbMorphology morphology) =>
        TryAnchoredErParadigm(
            scenario,
            HasModalAnchor,
            desiredEnding: "er",
            morphology,
            desiredMode: "inf",
            desiredTense: string.Empty);

    private static string? TryAnchoredErParadigm(
        DecisionEvaluationScenario scenario,
        Func<IReadOnlyList<string>, int, bool> hasAnchor,
        string desiredEnding,
        VerbMorphology morphology,
        string desiredMode,
        string desiredTense)
    {
        var matches = new List<string>();
        foreach (int slotIndex in scenario.Candidates
            .Select(static candidate => candidate.SlotIndex)
            .Distinct())
        {
            if (!hasAnchor(scenario.Transaction.Words, slotIndex))
                continue;

            string? identity = TrySelectCompleteErParadigm(
                scenario,
                slotIndex,
                desiredEnding,
                morphology,
                desiredMode,
                desiredTense);
            if (identity is not null)
                matches.Add(identity);
        }

        return matches.Distinct(StringComparer.Ordinal).Count() == 1
            ? matches[0]
            : null;
    }

    private static string? TrySelectCompleteErParadigm(
        DecisionEvaluationScenario scenario,
        int slotIndex,
        string desiredEnding,
        VerbMorphology morphology,
        string desiredMode,
        string desiredTense)
    {
        string literal = scenario.Transaction.Words[slotIndex];
        if (!TrySplitErParadigm(literal, out string literalStem, out string literalEnding))
            return null;

        DecisionCandidate[] candidates = scenario.Candidates
            .Where(candidate => candidate.SlotIndex == slotIndex)
            .ToArray();
        var endings = new HashSet<string>(StringComparer.Ordinal)
        {
            literalEnding,
        };
        string? selected = null;
        foreach (DecisionCandidate candidate in candidates)
        {
            if (!TrySplitErParadigm(
                    candidate.Replacement,
                    out string candidateStem,
                    out string candidateEnding)
                || !string.Equals(literalStem, candidateStem, StringComparison.Ordinal))
            {
                return null;
            }

            endings.Add(candidateEnding);
            if (!string.Equals(candidateEnding, desiredEnding, StringComparison.Ordinal))
                continue;
            if (!morphology.Analyses(candidate.Replacement.ToLowerInvariant())
                .Any(reading => string.Equals(
                        reading.Mode,
                        desiredMode,
                        StringComparison.Ordinal)
                    && string.Equals(
                        reading.Tense,
                        desiredTense,
                        StringComparison.Ordinal)))
            {
                continue;
            }
            if (selected is not null)
                return null;
            selected = candidate.Identity;
        }

        return endings.SetEquals(new[] { "er", "é", "ez" })
            ? selected
            : null;
    }

    private static string? TryAvoirDuBeforeInfinitive(
        DecisionEvaluationScenario scenario,
        VerbMorphology morphology)
    {
        var matches = new List<string>();
        foreach (DecisionCandidate candidate in scenario.Candidates)
        {
            int slotIndex = candidate.SlotIndex;
            if (slotIndex <= 0
                || slotIndex >= scenario.Transaction.Words.Count - 1
                || !string.Equals(
                    scenario.Transaction.Words[slotIndex],
                    "du",
                    StringComparison.Ordinal)
                || !string.Equals(candidate.Replacement, "dû", StringComparison.Ordinal)
                || !AvoirForms.Contains(ApostropheTail(
                    scenario.Transaction.Words[slotIndex - 1]))
                || !IsUnambiguousInfinitive(
                    morphology,
                    scenario.Transaction.Words[slotIndex + 1]))
            {
                continue;
            }

            matches.Add(candidate.Identity);
        }

        return matches.Distinct(StringComparer.Ordinal).Count() == 1
            ? matches[0]
            : null;
    }

    private static string? TryRestrictiveNYAQuaBeforeInfinitive(
        DecisionEvaluationScenario scenario,
        VerbMorphology morphology)
    {
        var matches = new List<string>();
        foreach (DecisionCandidate candidate in scenario.Candidates)
        {
            int slotIndex = candidate.SlotIndex;
            if (slotIndex < 2
                || slotIndex >= scenario.Transaction.Words.Count - 1
                || !string.Equals(
                    scenario.Transaction.Words[slotIndex - 2],
                    "n'y",
                    StringComparison.Ordinal)
                || !string.Equals(
                    scenario.Transaction.Words[slotIndex - 1],
                    "a",
                    StringComparison.Ordinal)
                || !string.Equals(
                    scenario.Transaction.Words[slotIndex],
                    "qu'a",
                    StringComparison.Ordinal)
                || !string.Equals(candidate.Replacement, "qu'à", StringComparison.Ordinal)
                || !IsUnambiguousInfinitive(
                    morphology,
                    scenario.Transaction.Words[slotIndex + 1]))
            {
                continue;
            }

            matches.Add(candidate.Identity);
        }

        return matches.Distinct(StringComparer.Ordinal).Count() == 1
            ? matches[0]
            : null;
    }

    private static void ValidateRuleOrder(IReadOnlyList<string> ruleOrder)
    {
        if (ruleOrder.Count != FrozenRuleIds.Count
            || ruleOrder.Distinct(StringComparer.Ordinal).Count() != FrozenRuleIds.Count
            || ruleOrder.Any(ruleId => !FrozenRuleIds.Contains(ruleId)))
        {
            throw new ArgumentException(
                "A rule order must contain each frozen rule exactly once.",
                nameof(ruleOrder));
        }
    }

    private static string ApostropheTail(string token)
    {
        string lower = token.ToLowerInvariant();
        int apostrophe = Math.Max(lower.LastIndexOf('\''), lower.LastIndexOf('’'));
        return apostrophe < 0 ? lower : lower[(apostrophe + 1)..];
    }

    private static bool HasStrongAvoirPastParticipleAnchor(
        IReadOnlyList<string> words,
        int slotIndex)
    {
        if (slotIndex <= 0 || slotIndex >= words.Count)
            return false;

        string previous = words[slotIndex - 1].ToLowerInvariant();
        if (previous is "j'ai" or "j’ai")
            return true;
        if (slotIndex < 2)
            return false;

        string subject = words[slotIndex - 2].ToLowerInvariant();
        return (previous, subject) switch
        {
            ("as", "tu") => true,
            ("a", "il" or "elle" or "on") => true,
            ("avons", "nous") => true,
            ("avez", "vous") => true,
            ("ont", "ils" or "elles") => true,
            _ => false,
        };
    }

    private static bool HasModalAnchor(
        IReadOnlyList<string> words,
        int slotIndex) =>
        slotIndex > 0
        && slotIndex < words.Count
        && ModalForms.Contains(ApostropheTail(words[slotIndex - 1]));

    private static bool TrySplitErParadigm(
        string value,
        out string stem,
        out string ending)
    {
        string lower = value.ToLowerInvariant();
        foreach (string candidateEnding in new[] { "er", "ez", "é" })
        {
            if (lower.Length <= candidateEnding.Length
                || !lower.EndsWith(candidateEnding, StringComparison.Ordinal))
            {
                continue;
            }

            stem = lower[..^candidateEnding.Length];
            ending = candidateEnding;
            return true;
        }

        stem = string.Empty;
        ending = string.Empty;
        return false;
    }

    private static bool IsUnambiguousInfinitive(
        VerbMorphology morphology,
        string value)
    {
        string lower = value.TrimEnd('.', ',', ';', ':', '!', '?').ToLowerInvariant();
        return !morphology.IsAmbiguous(lower)
            && morphology.Analyses(lower).Any(static reading => reading.Mode == "inf");
    }
}

internal static class SentenceUnanimityRuleIds
{
    public const string ExistingTerminalLocativeLa = "existing_terminal_locative_la";
    public const string AvoirPastParticiple = "avoir_past_participle_er_e";
    public const string ModalInfinitive = "modal_infinitive_er_e_ez";
    public const string AvoirDuBeforeInfinitive = "avoir_du_before_infinitive";
    public const string RestrictiveNYAQuaBeforeInfinitive =
        "restrictive_n_y_a_qu_a_before_infinitive";
}

internal readonly record struct SentenceRuleClaim(
    string RuleId,
    string CandidateIdentity);
