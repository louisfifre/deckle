using System.Diagnostics;
using System.Text.Json;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceDecisionInventoryCommand
{
    public const int Seed = 20260730;
    public const int WarmupEvaluationCount = 1_000;
    public const int MeasuredEvaluationCount = 10_000;

    public static int Run()
    {
        try
        {
            SentenceDecisionInventoryReport report = Measure();
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));
            return report.Validity.Valid ? 0 : 3;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    internal static SentenceDecisionInventoryReport Measure()
    {
        IReadOnlyList<DecisionInventoryEntry> inventory =
            SentenceDecisionInventory.Build();
        IReadOnlyList<DecisionEvaluationScenario> scenarios =
            SentenceDecisionInventoryEvaluation.BuildScenarios(inventory);
        DecisionInventoryBaseline baseline =
            SentenceDecisionInventoryEvaluation.EvaluateBaseline(inventory, scenarios);

        int[] schedule = BuildSchedule(
            scenarios.Count,
            WarmupEvaluationCount + MeasuredEvaluationCount,
            Seed);
        var reranker = new FrenchSentenceReranker();
        for (int index = 0; index < WarmupEvaluationCount; index++)
            _ = SentenceDecisionInventoryEvaluation.Evaluate(
                reranker,
                scenarios[schedule[index]]);

        var samples = new DecisionTimingSample[MeasuredEvaluationCount];
        Process process = Process.GetCurrentProcess();
        process.Refresh();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long privateMemoryBefore = process.PrivateMemorySize64;
        long workingSetBefore = process.WorkingSet64;

        for (int sampleIndex = 0;
            sampleIndex < MeasuredEvaluationCount;
            sampleIndex++)
        {
            int scenarioIndex = schedule[WarmupEvaluationCount + sampleIndex];
            DecisionEvaluationScenario scenario = scenarios[scenarioIndex];
            long started = Stopwatch.GetTimestamp();
            DecisionVerdict verdict = SentenceDecisionInventoryEvaluation.Evaluate(
                reranker,
                scenario);
            long elapsed = Stopwatch.GetTimestamp() - started;
            samples[sampleIndex] = new DecisionTimingSample(
                sampleIndex,
                scenario.PublicOrdinal,
                scenario.PermutationOrdinal,
                elapsed,
                SentenceDecisionInventoryEvaluation.VerdictName(verdict.Kind));
        }

        long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        process.Refresh();
        TimeSpan cpuAfter = process.TotalProcessorTime;
        long privateMemoryAfter = process.PrivateMemorySize64;
        long workingSetAfter = process.WorkingSet64;

        long[] sampleTicks = samples.Select(static sample => sample.ElapsedTicks).ToArray();
        MissingGroupingFieldsReport missingGroups = new(
            ParentGroup: inventory.Count(static entry =>
                entry.Provenance.ParentGroupId is null),
            SourceSessionGroup: inventory.Count(static entry =>
                entry.Provenance.SourceSessionGroupId is null),
            PunctuationVariantGroup: inventory.Count(static entry =>
                entry.Provenance.PunctuationVariantGroupId is null));
        CandidateFamilyReport[] candidateFamilies = inventory
            .GroupBy(static entry => entry.Provenance.CandidateFamilyGroup)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => new CandidateFamilyReport(group.Key, group.Count()))
            .ToArray();
        DecisionInventoryValidity validity = new(
            PublicCaseCountMatches: baseline.PublicCaseCount == 35,
            EveryNonliteralCandidateReconstructedExactly:
                baseline.NonliteralCandidateCount == 54,
            CandidateFamiliesFrozen:
                DecisionCandidateFamilies.FrozenAssignments.Count == inventory.Count
                && inventory.All(entry => string.Equals(
                    entry.Provenance.CandidateFamilyGroup,
                    DecisionCandidateFamilies.ForPublicCase(
                        entry.Provenance.PublicCaseId),
                    StringComparison.Ordinal))
                && candidateFamilies
                    .Select(static family => family.Name)
                    .SequenceEqual(
                        DecisionCandidateFamilies.Frozen.Order(StringComparer.Ordinal)),
            MissingGroupsReported:
                missingGroups.ParentGroup == baseline.PublicCaseCount
                && missingGroups.SourceSessionGroup == baseline.PublicCaseCount
                && missingGroups.PunctuationVariantGroup == baseline.PublicCaseCount,
            EveryPermutationVerdictIdentityStable:
                baseline.AllPermutationVerdictsIdentityStable,
            EveryCandidateOrderExhaustive:
                baseline.AllCandidateOrdersExhaustive,
            RawTimingSampleCountMatches:
                samples.Length == MeasuredEvaluationCount,
            ResourceDeltasCaptured:
                allocatedAfter >= allocatedBefore
                && cpuAfter >= cpuBefore
                && privateMemoryBefore > 0
                && privateMemoryAfter > 0);

        return new SentenceDecisionInventoryReport(
            "ACX-0019",
            SentenceDecisionInventory.SourceCorpus,
            Seed,
            Stopwatch.Frequency,
            new DecisionSchemaReport(
                nameof(DecisionInput),
                nameof(DecisionTruth),
                nameof(DecisionProvenance)),
            candidateFamilies,
            missingGroups,
            baseline,
            new DecisionTimingReport(
                WarmupEvaluationCount,
                samples.Length,
                NearestRankMilliseconds(sampleTicks, 0.50),
                NearestRankMilliseconds(sampleTicks, 0.95),
                NearestRankMilliseconds(sampleTicks, 0.99),
                sampleTicks.Max() * 1000.0 / Stopwatch.Frequency,
                samples),
            new DecisionResourceReport(
                allocatedBefore,
                allocatedAfter,
                allocatedAfter - allocatedBefore,
                cpuBefore.Ticks,
                cpuAfter.Ticks,
                (cpuAfter - cpuBefore).TotalMilliseconds,
                privateMemoryBefore,
                privateMemoryAfter,
                privateMemoryAfter - privateMemoryBefore,
                workingSetBefore,
                workingSetAfter,
                workingSetAfter - workingSetBefore,
                GpuWorkMeasured: false,
                GpuWorkClaim: "none"),
            validity,
            "Public visible development corpus only. This run may establish exact inventory reconstruction, candidate-order invariance, internal deterministic decisions, and warm in-process rule cost. It forbids grouped-validation, field-quality, applied-correction, UIA, injection, observed-target, end-to-end, physical-latency, or GPU-work claims.");
    }

    internal static int[] BuildSchedule(int scenarioCount, int evaluationCount, int seed)
    {
        if (scenarioCount < 1)
            throw new ArgumentOutOfRangeException(nameof(scenarioCount));
        if (evaluationCount < 1)
            throw new ArgumentOutOfRangeException(nameof(evaluationCount));

        var random = new Random(seed);
        var schedule = new int[evaluationCount];
        var block = Enumerable.Range(0, scenarioCount).ToArray();
        int written = 0;
        while (written < schedule.Length)
        {
            for (int index = block.Length - 1; index > 0; index--)
            {
                int swap = random.Next(index + 1);
                (block[index], block[swap]) = (block[swap], block[index]);
            }

            int count = Math.Min(block.Length, schedule.Length - written);
            Array.Copy(block, 0, schedule, written, count);
            written += count;
        }
        return schedule;
    }

    private static double NearestRankMilliseconds(
        IEnumerable<long> ticks,
        double percentile)
    {
        long[] sorted = ticks.Order().ToArray();
        if (sorted.Length == 0)
            throw new InvalidOperationException("No timing samples were recorded.");
        int index = Math.Max(0, (int)Math.Ceiling(sorted.Length * percentile) - 1);
        return sorted[index] * 1000.0 / Stopwatch.Frequency;
    }
}

internal sealed record SentenceDecisionInventoryReport(
    string ExperimentId,
    string Corpus,
    int Seed,
    long StopwatchFrequency,
    DecisionSchemaReport Schemas,
    IReadOnlyList<CandidateFamilyReport> CandidateFamilies,
    MissingGroupingFieldsReport MissingGroupingFields,
    DecisionInventoryBaseline DeterministicLocativeBaseline,
    DecisionTimingReport Timing,
    DecisionResourceReport Resources,
    DecisionInventoryValidity Validity,
    string ClaimBoundary);

internal sealed record DecisionSchemaReport(
    string Input,
    string Truth,
    string Provenance);

internal sealed record CandidateFamilyReport(string Name, int PublicCaseCount);

internal sealed record MissingGroupingFieldsReport(
    int ParentGroup,
    int SourceSessionGroup,
    int PunctuationVariantGroup);

internal sealed record DecisionTimingReport(
    int WarmupEvaluationCount,
    int MeasuredEvaluationCount,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaxMilliseconds,
    IReadOnlyList<DecisionTimingSample> RawSamples);

internal readonly record struct DecisionTimingSample(
    int SampleIndex,
    int PublicOrdinal,
    int PermutationOrdinal,
    long ElapsedTicks,
    string Verdict);

internal sealed record DecisionResourceReport(
    long ManagedAllocatedBytesBefore,
    long ManagedAllocatedBytesAfter,
    long ManagedAllocatedBytesDelta,
    long ProcessCpuTicksBefore,
    long ProcessCpuTicksAfter,
    double ProcessCpuMillisecondsDelta,
    long PrivateMemoryBytesBefore,
    long PrivateMemoryBytesAfter,
    long PrivateMemoryBytesDelta,
    long WorkingSetBytesBefore,
    long WorkingSetBytesAfter,
    long WorkingSetBytesDelta,
    bool GpuWorkMeasured,
    string GpuWorkClaim);

internal sealed record DecisionInventoryValidity(
    bool PublicCaseCountMatches,
    bool EveryNonliteralCandidateReconstructedExactly,
    bool CandidateFamiliesFrozen,
    bool MissingGroupsReported,
    bool EveryPermutationVerdictIdentityStable,
    bool EveryCandidateOrderExhaustive,
    bool RawTimingSampleCountMatches,
    bool ResourceDeltasCaptured)
{
    public bool Valid =>
        PublicCaseCountMatches
        && EveryNonliteralCandidateReconstructedExactly
        && CandidateFamiliesFrozen
        && MissingGroupsReported
        && EveryPermutationVerdictIdentityStable
        && EveryCandidateOrderExhaustive
        && RawTimingSampleCountMatches
        && ResourceDeltasCaptured;
}
