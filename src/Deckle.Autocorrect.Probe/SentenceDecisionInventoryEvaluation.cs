using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceDecisionInventoryEvaluation
{
    public static IReadOnlyList<DecisionEvaluationScenario> BuildScenarios(
        IReadOnlyList<DecisionInventoryEntry> inventory)
    {
        var scenarios = new List<DecisionEvaluationScenario>();
        foreach (DecisionInventoryEntry entry in inventory)
        {
            int permutationOrdinal = 0;
            foreach (DecisionCandidate[] permutation in Permutations(entry.Input.Candidates))
            {
                scenarios.Add(new DecisionEvaluationScenario(
                    entry.PublicOrdinal,
                    permutationOrdinal++,
                    new ClosedSentenceTransaction(
                        entry.Input.Literal,
                        entry.Input.Tokens,
                        permutation.Select(static candidate =>
                            candidate.ToSentenceEditCandidate()).ToArray()),
                    permutation));
            }
        }
        return scenarios;
    }

    public static DecisionInventoryBaseline EvaluateBaseline(
        IReadOnlyList<DecisionInventoryEntry> inventory,
        IReadOnlyList<DecisionEvaluationScenario> scenarios)
    {
        var reranker = new FrenchSentenceReranker();
        var caseReports = new List<DecisionInventoryCaseReport>(inventory.Count);
        int permutationEvaluations = 0;
        bool allStable = true;
        bool allExhaustive = true;

        foreach (DecisionInventoryEntry entry in inventory)
        {
            DecisionEvaluationScenario[] caseScenarios = scenarios
                .Where(scenario => scenario.PublicOrdinal == entry.PublicOrdinal)
                .OrderBy(static scenario => scenario.PermutationOrdinal)
                .ToArray();
            if (caseScenarios.Length == 0)
                throw new InvalidOperationException("An inventory entry has no permutation scenario.");

            DecisionVerdict[] verdicts = caseScenarios
                .Select(scenario => Evaluate(reranker, scenario))
                .ToArray();
            permutationEvaluations += verdicts.Length;
            bool stable = verdicts.Skip(1).All(verdict => verdict == verdicts[0]);
            allStable &= stable;
            int expectedPermutationCount = Factorial(entry.Input.Candidates.Count);
            int distinctPermutationCount = caseScenarios
                .Select(static scenario => string.Join(
                    '\u001f',
                    scenario.Candidates.Select(static candidate => candidate.Identity)))
                .Distinct(StringComparer.Ordinal)
                .Count();
            bool exhaustive = caseScenarios.Length == expectedPermutationCount
                && distinctPermutationCount == expectedPermutationCount;
            allExhaustive &= exhaustive;

            DecisionVerdict verdict = verdicts[0];
            string final = verdict.Kind == DecisionVerdictKind.Edit
                ? verdict.FinalText!
                : entry.Input.Literal;
            bool correct = entry.Truth.AcceptableFinals.Contains(
                final,
                StringComparer.Ordinal);
            caseReports.Add(new DecisionInventoryCaseReport(
                entry.PublicOrdinal,
                entry.Provenance.PublicCaseId,
                entry.Provenance.PublicCategory,
                entry.Provenance.SourceCorpus,
                entry.Provenance.CandidateFamilyGroup,
                entry.Provenance.ParentGroupId,
                entry.Provenance.SourceSessionGroupId,
                entry.Provenance.PunctuationVariantGroupId,
                entry.Input.Literal.Length,
                entry.Input.Tokens.Count,
                entry.Input.Candidates.Count,
                caseScenarios.Length,
                expectedPermutationCount,
                distinctPermutationCount,
                entry.Truth.RequiresEdit,
                VerdictName(verdict.Kind),
                verdict.CandidateIdentity,
                verdict.AbstainReason,
                correct,
                stable,
                exhaustive));
        }

        int correctEdits = caseReports.Count(report =>
            report.Verdict == "edit" && report.Correct);
        int wrongEdits = caseReports.Count(report =>
            report.Verdict == "edit" && !report.Correct);
        int affirmativeKeeps = caseReports.Count(static report => report.Verdict == "keep");
        int usefulAbstentions = caseReports.Count(report =>
            report.Verdict == "abstain" && !report.RequiresEdit);
        int regrettableAbstentions = caseReports.Count(report =>
            report.Verdict == "abstain" && report.RequiresEdit);

        return new DecisionInventoryBaseline(
            inventory.Count,
            inventory.Count(static entry => entry.Truth.RequiresEdit),
            inventory.Count(static entry => !entry.Truth.RequiresEdit),
            inventory.Sum(static entry => entry.Input.Candidates.Count),
            permutationEvaluations,
            allStable,
            allExhaustive,
            correctEdits,
            wrongEdits,
            affirmativeKeeps,
            usefulAbstentions,
            regrettableAbstentions,
            new AlwaysAbstainBaseline(
                UsefulAbstentions: inventory.Count(static entry => !entry.Truth.RequiresEdit),
                RegrettableAbstentions: inventory.Count(static entry => entry.Truth.RequiresEdit),
                PrecisionMeasured: false),
            caseReports);
    }

    public static DecisionVerdict Evaluate(
        FrenchSentenceReranker reranker,
        DecisionEvaluationScenario scenario)
    {
        RerankOutcome outcome = reranker.RerankSentence(scenario.Transaction);
        return Classify(outcome, scenario);
    }

    internal static DecisionVerdict Classify(
        RerankOutcome outcome,
        DecisionEvaluationScenario scenario)
    {
        if (outcome.Chosen is null)
        {
            return outcome.AbstainReason is null
                ? new DecisionVerdict(
                    DecisionVerdictKind.Keep,
                    CandidateIdentity: null,
                    FinalText: scenario.Transaction.Literal,
                    AbstainReason: null)
                : new DecisionVerdict(
                    DecisionVerdictKind.Abstain,
                    CandidateIdentity: null,
                    FinalText: null,
                    outcome.AbstainReason);
        }

        DecisionCandidate[] matches = scenario.Candidates
            .Where(candidate =>
                string.Equals(candidate.Replacement, outcome.Chosen, StringComparison.Ordinal)
                && (outcome.ChosenSlotIndex is null
                    || outcome.ChosenSlotIndex == candidate.SlotIndex))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("A changing verdict did not identify one submitted edit.");

        DecisionCandidate chosen = matches[0];
        return new DecisionVerdict(
            DecisionVerdictKind.Edit,
            chosen.Identity,
            SentenceDecisionInventory.Apply(scenario.Transaction.Literal, chosen),
            AbstainReason: null);
    }

    internal static string VerdictName(DecisionVerdictKind kind) => kind switch
    {
        DecisionVerdictKind.Keep => "keep",
        DecisionVerdictKind.Edit => "edit",
        DecisionVerdictKind.Abstain => "abstain",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static int Factorial(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        int result = 1;
        for (int factor = 2; factor <= value; factor++)
            result *= factor;
        return result;
    }

    private static IEnumerable<DecisionCandidate[]> Permutations(
        IReadOnlyList<DecisionCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            yield return [];
            yield break;
        }

        var buffer = candidates.ToArray();
        foreach (DecisionCandidate[] permutation in Permute(buffer, 0))
            yield return permutation;
    }

    private static IEnumerable<DecisionCandidate[]> Permute(
        DecisionCandidate[] buffer,
        int start)
    {
        if (start == buffer.Length)
        {
            yield return [.. buffer];
            yield break;
        }

        for (int index = start; index < buffer.Length; index++)
        {
            (buffer[start], buffer[index]) = (buffer[index], buffer[start]);
            foreach (DecisionCandidate[] permutation in Permute(buffer, start + 1))
                yield return permutation;
            (buffer[start], buffer[index]) = (buffer[index], buffer[start]);
        }
    }
}

internal enum DecisionVerdictKind
{
    Keep,
    Edit,
    Abstain,
}

internal readonly record struct DecisionVerdict(
    DecisionVerdictKind Kind,
    string? CandidateIdentity,
    string? FinalText,
    string? AbstainReason);

internal sealed record DecisionEvaluationScenario(
    int PublicOrdinal,
    int PermutationOrdinal,
    ClosedSentenceTransaction Transaction,
    IReadOnlyList<DecisionCandidate> Candidates);

internal sealed record DecisionInventoryBaseline(
    int PublicCaseCount,
    int CorrectableCaseCount,
    int LiteralCaseCount,
    int NonliteralCandidateCount,
    int PermutationEvaluationCount,
    bool AllPermutationVerdictsIdentityStable,
    bool AllCandidateOrdersExhaustive,
    int CorrectEditCount,
    int WrongEditCount,
    int AffirmativeKeepCount,
    int UsefulAbstentionCount,
    int RegrettableAbstentionCount,
    AlwaysAbstainBaseline AlwaysAbstain,
    IReadOnlyList<DecisionInventoryCaseReport> Cases);

internal sealed record AlwaysAbstainBaseline(
    int UsefulAbstentions,
    int RegrettableAbstentions,
    bool PrecisionMeasured);

internal sealed record DecisionInventoryCaseReport(
    int PublicOrdinal,
    string PublicCaseId,
    string PublicCategory,
    string SourceCorpus,
    string CandidateFamilyGroup,
    string? ParentGroupId,
    string? SourceSessionGroupId,
    string? PunctuationVariantGroupId,
    int LiteralUtf16Length,
    int TokenCount,
    int CandidateCount,
    int PermutationCount,
    int ExpectedPermutationCount,
    int DistinctPermutationCount,
    bool RequiresEdit,
    string Verdict,
    string? CandidateIdentity,
    string? AbstainReason,
    bool Correct,
    bool PermutationVerdictIdentityStable,
    bool CandidateOrdersExhaustive);
