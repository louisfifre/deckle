using System.Collections.Immutable;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceUnanimityBundleEvaluation
{
    public static ImmutableArray<ImmutableArray<string>> BuildRuleOrders()
    {
        string[] rules = SentenceUnanimityBundle.FrozenRuleOrder.ToArray();
        return Permute(rules, 0)
            .Select(static order => order.ToImmutableArray())
            .ToImmutableArray();
    }

    public static SentenceUnanimityBundleBaseline Evaluate(
        IReadOnlyList<DecisionInventoryEntry> inventory,
        IReadOnlyList<DecisionEvaluationScenario> candidateScenarios,
        ImmutableArray<ImmutableArray<string>> ruleOrders,
        DecisionInventoryBaseline locativeBaseline,
        VerbMorphology morphology)
    {
        int expectedRuleOrderCount = SentenceDecisionInventoryEvaluation.Factorial(
            SentenceUnanimityBundle.FrozenRuleIds.Count);
        int distinctRuleOrderCount = ruleOrders
            .Select(static order => string.Join('\u001f', order))
            .Distinct(StringComparer.Ordinal)
            .Count();
        bool allRuleOrdersExhaustive = ruleOrders.Length == expectedRuleOrderCount
            && distinctRuleOrderCount == expectedRuleOrderCount;

        var caseReports = new List<SentenceUnanimityCaseReport>(inventory.Count);
        int jointEvaluationCount = 0;
        bool allJointVerdictsStable = true;

        foreach (DecisionInventoryEntry entry in inventory)
        {
            DecisionEvaluationScenario[] scenarios = candidateScenarios
                .Where(scenario => scenario.PublicOrdinal == entry.PublicOrdinal)
                .OrderBy(static scenario => scenario.PermutationOrdinal)
                .ToArray();
            if (scenarios.Length == 0)
                throw new InvalidOperationException("An inventory entry has no candidate order.");

            var verdicts = new List<DecisionVerdict>(scenarios.Length * ruleOrders.Length);
            foreach (DecisionEvaluationScenario scenario in scenarios)
            {
                foreach (ImmutableArray<string> ruleOrder in ruleOrders)
                {
                    verdicts.Add(SentenceUnanimityBundle.Evaluate(
                        scenario,
                        ruleOrder,
                        morphology));
                }
            }
            jointEvaluationCount += verdicts.Count;

            DecisionVerdict canonical = verdicts[0];
            bool stable = verdicts.Skip(1).All(verdict => verdict == canonical);
            allJointVerdictsStable &= stable;
            string final = canonical.Kind == DecisionVerdictKind.Edit
                ? canonical.FinalText!
                : entry.Input.Literal;
            bool correct = entry.Truth.AcceptableFinals.Contains(
                final,
                StringComparer.Ordinal);
            IReadOnlyList<SentenceRuleClaim> claims =
                SentenceUnanimityBundle.CollectClaims(
                    scenarios[0],
                    SentenceUnanimityBundle.FrozenRuleOrder,
                    morphology);

            caseReports.Add(new SentenceUnanimityCaseReport(
                entry.PublicOrdinal,
                entry.Provenance.PublicCaseId,
                entry.Provenance.CandidateFamilyGroup,
                entry.Truth.RequiresEdit,
                SentenceDecisionInventoryEvaluation.VerdictName(canonical.Kind),
                canonical.CandidateIdentity,
                canonical.AbstainReason,
                claims.Select(static claim => claim.RuleId).ToArray(),
                correct,
                scenarios.Length,
                ruleOrders.Length,
                verdicts.Count,
                stable));
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
        HashSet<string> baselineCorrectIds = locativeBaseline.Cases
            .Where(report => report.Verdict == "edit" && report.Correct)
            .Select(static report => report.PublicCaseId)
            .ToHashSet(StringComparer.Ordinal);
        SentenceUnanimityCaseReport[] residualCorrect = caseReports
            .Where(report => report.Verdict == "edit"
                && report.Correct
                && !baselineCorrectIds.Contains(report.PublicCaseId))
            .ToArray();

        return new SentenceUnanimityBundleBaseline(
            inventory.Count,
            candidateScenarios.Count,
            ruleOrders.Length,
            jointEvaluationCount,
            expectedRuleOrderCount,
            distinctRuleOrderCount,
            allRuleOrdersExhaustive,
            allJointVerdictsStable,
            correctEdits,
            wrongEdits,
            affirmativeKeeps,
            usefulAbstentions,
            regrettableAbstentions,
            residualCorrect.Length,
            residualCorrect
                .Select(static report => report.CandidateFamilyGroup)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            caseReports);
    }

    public static IReadOnlyList<SentenceUnanimityEvaluationScenario> BuildJointScenarios(
        IReadOnlyList<DecisionEvaluationScenario> candidateScenarios,
        ImmutableArray<ImmutableArray<string>> ruleOrders)
    {
        var scenarios = new List<SentenceUnanimityEvaluationScenario>(
            candidateScenarios.Count * ruleOrders.Length);
        for (int candidateIndex = 0; candidateIndex < candidateScenarios.Count; candidateIndex++)
        {
            for (int ruleOrderIndex = 0; ruleOrderIndex < ruleOrders.Length; ruleOrderIndex++)
            {
                scenarios.Add(new SentenceUnanimityEvaluationScenario(
                    candidateScenarios[candidateIndex],
                    ruleOrderIndex,
                    ruleOrders[ruleOrderIndex]));
            }
        }
        return scenarios;
    }

    private static IEnumerable<string[]> Permute(string[] buffer, int start)
    {
        if (start == buffer.Length)
        {
            yield return [.. buffer];
            yield break;
        }

        for (int index = start; index < buffer.Length; index++)
        {
            (buffer[start], buffer[index]) = (buffer[index], buffer[start]);
            foreach (string[] permutation in Permute(buffer, start + 1))
                yield return permutation;
            (buffer[start], buffer[index]) = (buffer[index], buffer[start]);
        }
    }
}

internal sealed record SentenceUnanimityBundleBaseline(
    int PublicCaseCount,
    int CandidateOrderCount,
    int RuleOrderCount,
    int JointEvaluationCount,
    int ExpectedRuleOrderCount,
    int DistinctRuleOrderCount,
    bool AllRuleOrdersExhaustive,
    bool AllJointVerdictsIdentityStable,
    int CorrectEditCount,
    int WrongEditCount,
    int AffirmativeKeepCount,
    int UsefulAbstentionCount,
    int RegrettableAbstentionCount,
    int CorrectResidualEditCount,
    int CorrectResidualFamilyCount,
    IReadOnlyList<SentenceUnanimityCaseReport> Cases);

internal sealed record SentenceUnanimityCaseReport(
    int PublicOrdinal,
    string PublicCaseId,
    string CandidateFamilyGroup,
    bool RequiresEdit,
    string Verdict,
    string? CandidateIdentity,
    string? AbstainReason,
    IReadOnlyList<string> ClaimingRuleIds,
    bool Correct,
    int CandidateOrderCount,
    int RuleOrderCount,
    int JointEvaluationCount,
    bool JointVerdictIdentityStable);

internal sealed record SentenceUnanimityEvaluationScenario(
    DecisionEvaluationScenario CandidateScenario,
    int RuleOrderOrdinal,
    ImmutableArray<string> RuleOrder);
