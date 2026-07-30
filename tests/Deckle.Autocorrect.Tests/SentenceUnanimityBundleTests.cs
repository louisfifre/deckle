using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "integration")]
public sealed class SentenceUnanimityBundleTests
{
    [Fact]
    public void ArgumentsSelectIsolatedUnanimityCommand()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(["--sentence-unanimity-bundle"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.SentenceUnanimityBundle, parsed.Mode);
        Assert.Empty(parsed.Models);
        Assert.Null(ProbeArguments.Parse(
            ["--sentence-unanimity-bundle", "--iterations", "2"]));
        Assert.Null(ProbeArguments.Parse(
            ["--sentence-unanimity-bundle", "--provider", "cpu"]));
    }

    [Fact]
    public void FrozenBundleAddsFourCorrectResidualEditsAcrossTwoFamilies()
    {
        SentenceUnanimityBundleReport report = SentenceUnanimityBundleCommand.Measure();

        Assert.True(report.Hypothesis.Passed);
        Assert.Equal(5, report.Bundle.CorrectEditCount);
        Assert.Equal(0, report.Bundle.WrongEditCount);
        Assert.Equal(0, report.Bundle.AffirmativeKeepCount);
        Assert.Equal(19, report.Bundle.UsefulAbstentionCount);
        Assert.Equal(11, report.Bundle.RegrettableAbstentionCount);
        Assert.Equal(4, report.Bundle.CorrectResidualEditCount);
        Assert.Equal(2, report.Bundle.CorrectResidualFamilyCount);
        Assert.Equal(
            new[]
            {
                "du_participle",
                "infinitive_after_pouvez",
                "participle_after_avoir",
                "qu_a_preposition",
            },
            report.Bundle.Cases
                .Where(static reportCase => reportCase.Verdict == "edit")
                .Select(static reportCase => reportCase.PublicCaseId)
                .Except(["la_location"], StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryEditIsOneExactSubmittedCandidate()
    {
        IReadOnlyList<DecisionInventoryEntry> inventory =
            SentenceDecisionInventory.Build();
        IReadOnlyList<DecisionEvaluationScenario> scenarios =
            SentenceDecisionInventoryEvaluation.BuildScenarios(inventory);
        SentenceUnanimityMorphologyResource morphology =
            SentenceUnanimityMorphology.Load();

        foreach (DecisionEvaluationScenario scenario in scenarios)
        {
            DecisionVerdict verdict = SentenceUnanimityBundle.Evaluate(
                scenario,
                SentenceUnanimityBundle.FrozenRuleOrder,
                morphology.Data);
            if (verdict.Kind != DecisionVerdictKind.Edit)
                continue;

            DecisionCandidate candidate = Assert.Single(
                scenario.Candidates,
                candidate => candidate.Identity == verdict.CandidateIdentity);
            Assert.Equal(
                SentenceDecisionInventory.Apply(
                    scenario.Transaction.Literal,
                    candidate),
                verdict.FinalText);
        }
    }

    [Fact]
    public void CandidateAndRuleOrdersAreExhaustiveAndIdentityStable()
    {
        SentenceUnanimityBundleReport report = SentenceUnanimityBundleCommand.Measure();

        Assert.Equal(66, report.Bundle.CandidateOrderCount);
        Assert.Equal(120, report.Bundle.RuleOrderCount);
        Assert.Equal(120, report.Bundle.ExpectedRuleOrderCount);
        Assert.Equal(120, report.Bundle.DistinctRuleOrderCount);
        Assert.Equal(7_920, report.Bundle.JointEvaluationCount);
        Assert.True(report.Bundle.AllRuleOrdersExhaustive);
        Assert.True(report.Bundle.AllJointVerdictsIdentityStable);
        Assert.All(report.Bundle.Cases, static reportCase =>
        {
            Assert.Equal(120, reportCase.RuleOrderCount);
            Assert.Equal(
                reportCase.CandidateOrderCount * reportCase.RuleOrderCount,
                reportCase.JointEvaluationCount);
            Assert.True(reportCase.JointVerdictIdentityStable);
        });
    }

    [Fact]
    public void DisagreementAbstainsRegardlessOfClaimOrder()
    {
        DecisionInventoryEntry entry = SentenceDecisionInventory.Build()
            .Single(entry => entry.Provenance.PublicCaseId == "participle_after_avoir");
        DecisionEvaluationScenario scenario =
            SentenceDecisionInventoryEvaluation.BuildScenarios([entry])[0];
        Assert.Equal(2, scenario.Candidates.Count);
        SentenceRuleClaim first = new("first", scenario.Candidates[0].Identity);
        SentenceRuleClaim second = new("second", scenario.Candidates[1].Identity);

        DecisionVerdict forward = SentenceUnanimityBundle.Resolve(
            scenario,
            [first, second]);
        DecisionVerdict reverse = SentenceUnanimityBundle.Resolve(
            scenario,
            [second, first]);

        Assert.Equal(DecisionVerdictKind.Abstain, forward.Kind);
        Assert.Equal(SentenceUnanimityBundle.RuleDisagreement, forward.AbstainReason);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void NoClaimAbstainsWithoutInventingKeep()
    {
        DecisionInventoryEntry entry = SentenceDecisionInventory.Build()
            .Single(entry => entry.Provenance.PublicCaseId == "literal_ratures");
        DecisionEvaluationScenario scenario =
            SentenceDecisionInventoryEvaluation.BuildScenarios([entry])[0];

        DecisionVerdict verdict = SentenceUnanimityBundle.Resolve(scenario, []);

        Assert.Equal(DecisionVerdictKind.Abstain, verdict.Kind);
        Assert.Null(verdict.CandidateIdentity);
        Assert.Null(verdict.FinalText);
        Assert.Equal("no_rule", verdict.AbstainReason);
    }

    [Fact]
    public void NominalInfinitiveAndNounSuffixTrapsAbstain()
    {
        SentenceUnanimityMorphologyResource morphology =
            SentenceUnanimityMorphology.Load();
        DecisionEvaluationScenario nominalInfinitive = Scenario(
            "c'est aller trop loin",
            ["c'est", "aller", "trop", "loin"],
            new DecisionCandidate("1@6:5=allé", 1, 6, 5, "allé"),
            new DecisionCandidate("1@6:5=allez", 1, 6, 5, "allez"));
        DecisionEvaluationScenario nounSuffix = Scenario(
            "j'ai du plaisir",
            ["j'ai", "du", "plaisir"],
            new DecisionCandidate("1@5:2=dû", 1, 5, 2, "dû"));

        DecisionVerdict nominalVerdict = SentenceUnanimityBundle.Evaluate(
            nominalInfinitive,
            SentenceUnanimityBundle.FrozenRuleOrder,
            morphology.Data);
        DecisionVerdict nounVerdict = SentenceUnanimityBundle.Evaluate(
            nounSuffix,
            SentenceUnanimityBundle.FrozenRuleOrder,
            morphology.Data);

        Assert.Equal(DecisionVerdictKind.Abstain, nominalVerdict.Kind);
        Assert.Equal(DecisionVerdictKind.Abstain, nounVerdict.Kind);
    }

    [Fact]
    public void ExistingLocativeBaselineRemainsSeparateAndUnchanged()
    {
        SentenceUnanimityBundleReport report = SentenceUnanimityBundleCommand.Measure();

        Assert.True(report.Hypothesis.ExistingBaselinePreserved);
        Assert.Equal(1, report.LocativeBaseline.CorrectEditCount);
        Assert.Equal(0, report.LocativeBaseline.WrongEditCount);
        Assert.Equal(19, report.LocativeBaseline.UsefulAbstentionCount);
        Assert.Equal(15, report.LocativeBaseline.RegrettableAbstentionCount);
    }

    [Fact]
    public void ReportRetainsMixedRawSamplesAndStrictClaimBoundary()
    {
        SentenceUnanimityBundleReport report = SentenceUnanimityBundleCommand.Measure();

        Assert.True(report.Validity.Valid);
        Assert.True(report.Validity.MorphologyArtifactCaptured);
        Assert.Equal("verbs-fr.tsv.gz", report.Morphology.ArtifactName);
        Assert.Equal(64, report.Morphology.Sha256.Length);
        Assert.True(report.Morphology.Bytes > 0);
        Assert.True(report.Morphology.FormCount > 0);
        Assert.Equal(0, report.Morphology.SkippedLines);
        Assert.Equal(10_000, report.Timing.MeasuredEvaluationCount);
        Assert.Equal(10_000, report.Timing.RawSamples.Count);
        Assert.True(report.Validity.MixedScheduleCoversEveryJointScenario);
        Assert.False(report.Resources.GpuWorkMeasured);
        Assert.Equal("none", report.Resources.GpuWorkClaim);
        Assert.Contains("grouped-validation", report.ClaimBoundary);
        Assert.Contains("field-quality", report.ClaimBoundary);
        Assert.Contains("applied-correction", report.ClaimBoundary);
        Assert.Contains("production-safety", report.ClaimBoundary);
        Assert.Contains("UIA", report.ClaimBoundary);
        Assert.Contains("end-to-end", report.ClaimBoundary);
    }

    private static DecisionEvaluationScenario Scenario(
        string literal,
        IReadOnlyList<string> words,
        params DecisionCandidate[] candidates) =>
        new(
            PublicOrdinal: -1,
            PermutationOrdinal: 0,
            new Deckle.Autocorrect.ClosedSentenceTransaction(
                literal,
                words,
                candidates.Select(static candidate =>
                    candidate.ToSentenceEditCandidate()).ToArray()),
            candidates);
}
