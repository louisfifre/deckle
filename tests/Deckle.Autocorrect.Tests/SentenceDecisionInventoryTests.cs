using Deckle.Autocorrect;
using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class SentenceDecisionInventoryTests
{
    [Fact]
    public void ArgumentsSelectIsolatedInventoryCommand()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(["--sentence-decision-inventory"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.SentenceDecisionInventory, parsed.Mode);
        Assert.Empty(parsed.Models);
        Assert.Null(ProbeArguments.Parse(
            ["--sentence-decision-inventory", "--iterations", "2"]));
        Assert.Null(ProbeArguments.Parse(
            ["--sentence-decision-inventory", "--provider", "cpu"]));
    }

    [Fact]
    public void InventorySeparatesSanitizedInputTruthAndProvenance()
    {
        IReadOnlyList<DecisionInventoryEntry> inventory =
            SentenceDecisionInventory.Build();

        Assert.Equal(35, inventory.Count);
        Assert.Equal(16, inventory.Count(static entry => entry.Truth.RequiresEdit));
        Assert.Equal(19, inventory.Count(static entry => !entry.Truth.RequiresEdit));
        Assert.Equal(54, inventory.Sum(static entry => entry.Input.Candidates.Count));
        Assert.Equal(
            new[] { "Candidates", "Literal", "Tokens" },
            typeof(DecisionInput).GetProperties()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "AcceptableFinals", "RequiresEdit" },
            typeof(DecisionTruth).GetProperties()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Contains(
            "PublicCaseId",
            typeof(DecisionProvenance).GetProperties()
                .Select(static property => property.Name));
        Assert.All(inventory, entry =>
        {
            CorrectionBenchmarkCase source =
                CorrectionBenchmarkCorpus.All[entry.PublicOrdinal];
            Assert.Equal(source.Literal, entry.Input.Literal);
            Assert.Equal(source.Gold, Assert.Single(entry.Truth.AcceptableFinals));
            Assert.Equal(source.RequiresCorrection, entry.Truth.RequiresEdit);
            Assert.Equal(source.Id, entry.Provenance.PublicCaseId);
            Assert.Equal(source.Category, entry.Provenance.PublicCategory);
            Assert.Equal(
                SentenceDecisionInventory.SourceCorpus,
                entry.Provenance.SourceCorpus);
            Assert.Null(entry.Provenance.ParentGroupId);
            Assert.Null(entry.Provenance.SourceSessionGroupId);
            Assert.Null(entry.Provenance.PunctuationVariantGroupId);
        });
    }

    [Fact]
    public void EveryPublicAlternativeIsOneExactUtf16TokenEdit()
    {
        IReadOnlyList<DecisionInventoryEntry> inventory =
            SentenceDecisionInventory.Build();

        foreach (DecisionInventoryEntry entry in inventory)
        {
            CorrectionBenchmarkCase source =
                CorrectionBenchmarkCorpus.All[entry.PublicOrdinal];
            string[] expected = source.Candidates
                .Where((_, index) => index != source.LiteralIndex)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] reconstructed = entry.Input.Candidates
                .Select(candidate =>
                {
                    Assert.Equal(
                        entry.Input.Tokens[candidate.SlotIndex],
                        entry.Input.Literal.Substring(candidate.Start, candidate.Length));
                    return SentenceDecisionInventory.Apply(entry.Input.Literal, candidate);
                })
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected, reconstructed);
            Assert.Equal(
                entry.Input.Candidates.Count,
                entry.Input.Candidates
                    .Select(static candidate => candidate.Identity)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
        }
    }

    [Fact]
    public void CandidateFamiliesAreFrozenWithoutPretendingGroupedProvenanceExists()
    {
        IReadOnlyList<DecisionInventoryEntry> inventory =
            SentenceDecisionInventory.Build();

        Assert.Equal(
            DecisionCandidateFamilies.Frozen.Order(StringComparer.Ordinal),
            inventory.Select(static entry => entry.Provenance.CandidateFamilyGroup)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        string[] expectedDiacriticOnly =
        [
            "la_location", "la_determiner", "a_auxiliary", "a_preposition",
            "ou_question", "ou_choice", "sur_certain", "sur_surface",
            "ca_subject", "du_participle", "du_article", "literal_la_build",
            "literal_a_variable", "literal_ou_api", "literal_ratures",
            "literal_date", "qu_a_auxiliary", "qu_a_preposition",
        ];
        string[] expectedTerminalInflection =
        [
            "participle_after_avoir", "infinitive_after_vais",
            "infinitive_after_pour", "participle_c_est", "infinitive_il_faut",
            "participle_adjective_trap", "second_plural_present",
            "infinitive_after_pouvez", "feminine_singular",
            "masculine_singular", "feminine_plural_participle",
            "masculine_plural_participle", "feminine_plural_subject",
            "masculine_plural_subject", "plural_adjective",
            "singular_adjective", "duplicate_letter",
        ];
        Assert.Equal(
            expectedDiacriticOnly.Order(StringComparer.Ordinal),
            inventory.Where(entry => entry.Provenance.CandidateFamilyGroup
                    == DecisionCandidateFamilies.DiacriticOnly)
                .Select(static entry => entry.Provenance.PublicCaseId)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            expectedTerminalInflection.Order(StringComparer.Ordinal),
            inventory.Where(entry => entry.Provenance.CandidateFamilyGroup
                    == DecisionCandidateFamilies.TerminalInflection)
                .Select(static entry => entry.Provenance.PublicCaseId)
                .Order(StringComparer.Ordinal));
        Assert.All(inventory, entry =>
        {
            Assert.Contains(
                entry.Provenance.CandidateFamilyGroup,
                (IReadOnlySet<string>)DecisionCandidateFamilies.Frozen);
            Assert.Null(entry.Provenance.ParentGroupId);
            Assert.Null(entry.Provenance.SourceSessionGroupId);
            Assert.Null(entry.Provenance.PunctuationVariantGroupId);
        });
    }

    [Fact]
    public void EveryCandidateOrderProducesTheSameIdentifiedVerdict()
    {
        IReadOnlyList<DecisionInventoryEntry> inventory =
            SentenceDecisionInventory.Build();
        IReadOnlyList<DecisionEvaluationScenario> scenarios =
            SentenceDecisionInventoryEvaluation.BuildScenarios(inventory);
        DecisionInventoryBaseline baseline =
            SentenceDecisionInventoryEvaluation.EvaluateBaseline(inventory, scenarios);

        Assert.Equal(66, scenarios.Count);
        Assert.Equal(66, baseline.PermutationEvaluationCount);
        Assert.True(baseline.AllPermutationVerdictsIdentityStable);
        Assert.True(baseline.AllCandidateOrdersExhaustive);
        Assert.All(baseline.Cases, static report =>
        {
            Assert.True(report.PermutationVerdictIdentityStable);
            Assert.True(report.CandidateOrdersExhaustive);
            Assert.Equal(
                SentenceDecisionInventoryEvaluation.Factorial(report.CandidateCount),
                report.ExpectedPermutationCount);
            Assert.Equal(report.ExpectedPermutationCount, report.PermutationCount);
            Assert.Equal(report.ExpectedPermutationCount, report.DistinctPermutationCount);
        });
    }

    [Fact]
    public void ExistingLocativeRuleBaselineIsMeasuredSeparatelyFromAlwaysAbstain()
    {
        IReadOnlyList<DecisionInventoryEntry> inventory =
            SentenceDecisionInventory.Build();
        DecisionInventoryBaseline baseline =
            SentenceDecisionInventoryEvaluation.EvaluateBaseline(
                inventory,
                SentenceDecisionInventoryEvaluation.BuildScenarios(inventory));

        Assert.Equal(1, baseline.CorrectEditCount);
        Assert.Equal(0, baseline.WrongEditCount);
        Assert.Equal(0, baseline.AffirmativeKeepCount);
        Assert.Equal(19, baseline.UsefulAbstentionCount);
        Assert.Equal(15, baseline.RegrettableAbstentionCount);
        Assert.Equal(19, baseline.AlwaysAbstain.UsefulAbstentions);
        Assert.Equal(16, baseline.AlwaysAbstain.RegrettableAbstentions);
        Assert.False(baseline.AlwaysAbstain.PrecisionMeasured);
    }

    [Fact]
    public void KeepEditAndAbstentionRemainDistinctVerdicts()
    {
        DecisionInventoryEntry entry = SentenceDecisionInventory.Build()[0];
        DecisionEvaluationScenario scenario =
            SentenceDecisionInventoryEvaluation.BuildScenarios([entry])[0];
        DecisionCandidate edit = Assert.Single(scenario.Candidates);

        DecisionVerdict keep = SentenceDecisionInventoryEvaluation.Classify(
            new RerankOutcome(
                null,
                Array.Empty<RerankCandidateScore>(),
                Margin: 1.0,
                Threshold: 0.0,
                AbstainReason: null),
            scenario);
        DecisionVerdict change = SentenceDecisionInventoryEvaluation.Classify(
            new RerankOutcome(
                edit.Replacement,
                Array.Empty<RerankCandidateScore>(),
                Margin: 1.0,
                Threshold: 0.0,
                AbstainReason: null)
            {
                ChosenSlotIndex = edit.SlotIndex,
            },
            scenario);
        DecisionVerdict abstain = SentenceDecisionInventoryEvaluation.Classify(
            RerankOutcome.Abstained(RerankOutcome.AbstainReasons.NoRule),
            scenario);

        Assert.Equal(DecisionVerdictKind.Keep, keep.Kind);
        Assert.Equal(DecisionVerdictKind.Edit, change.Kind);
        Assert.Equal(edit.Identity, change.CandidateIdentity);
        Assert.Equal(DecisionVerdictKind.Abstain, abstain.Kind);
        Assert.NotNull(abstain.AbstainReason);
    }

    [Fact]
    public void SeededWarmScheduleIsDeterministicMixedAndComplete()
    {
        const int scenarioCount = 66;
        int evaluationCount = SentenceDecisionInventoryCommand.WarmupEvaluationCount
            + SentenceDecisionInventoryCommand.MeasuredEvaluationCount;

        int[] first = SentenceDecisionInventoryCommand.BuildSchedule(
            scenarioCount,
            evaluationCount,
            SentenceDecisionInventoryCommand.Seed);
        int[] second = SentenceDecisionInventoryCommand.BuildSchedule(
            scenarioCount,
            evaluationCount,
            SentenceDecisionInventoryCommand.Seed);

        Assert.Equal(evaluationCount, first.Length);
        Assert.Equal(first, second);
        Assert.All(first, index => Assert.InRange(index, 0, scenarioCount - 1));
        Assert.Equal(
            Enumerable.Range(0, scenarioCount),
            first.Skip(SentenceDecisionInventoryCommand.WarmupEvaluationCount)
                .Distinct()
                .Order());
    }

    [Fact]
    public void ReportRetainsTenThousandRawWarmSamplesAndForbidsGpuClaim()
    {
        SentenceDecisionInventoryReport report =
            SentenceDecisionInventoryCommand.Measure();

        Assert.True(report.Validity.Valid);
        Assert.Equal(10_000, report.Timing.MeasuredEvaluationCount);
        Assert.Equal(10_000, report.Timing.RawSamples.Count);
        Assert.False(report.Resources.GpuWorkMeasured);
        Assert.Equal("none", report.Resources.GpuWorkClaim);
        Assert.True(report.Resources.ManagedAllocatedBytesAfter
            >= report.Resources.ManagedAllocatedBytesBefore);
        Assert.Equal(
            report.Resources.ManagedAllocatedBytesAfter
                - report.Resources.ManagedAllocatedBytesBefore,
            report.Resources.ManagedAllocatedBytesDelta);
        Assert.True(report.Resources.ProcessCpuTicksAfter
            >= report.Resources.ProcessCpuTicksBefore);
        Assert.Equal(
            TimeSpan.FromTicks(
                report.Resources.ProcessCpuTicksAfter
                    - report.Resources.ProcessCpuTicksBefore).TotalMilliseconds,
            report.Resources.ProcessCpuMillisecondsDelta);
        Assert.All(report.DeterministicLocativeBaseline.Cases, static reportCase =>
        {
            Assert.Equal(
                SentenceDecisionInventory.SourceCorpus,
                reportCase.SourceCorpus);
            Assert.Null(reportCase.ParentGroupId);
            Assert.Null(reportCase.SourceSessionGroupId);
            Assert.Null(reportCase.PunctuationVariantGroupId);
        });
        Assert.Contains("forbids grouped-validation", report.ClaimBoundary);
        Assert.Contains("field-quality", report.ClaimBoundary);
        Assert.Contains("applied-correction", report.ClaimBoundary);
        Assert.Contains("UIA", report.ClaimBoundary);
        Assert.Contains("end-to-end", report.ClaimBoundary);
    }
}
