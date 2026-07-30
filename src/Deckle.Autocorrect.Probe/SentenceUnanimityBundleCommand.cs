using System.Diagnostics;
using System.Collections.Immutable;
using System.Text.Json;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceUnanimityBundleCommand
{
    public const int Seed = 20260730;
    public const int WarmupEvaluationCount = 1_000;
    public const int MeasuredEvaluationCount = 10_000;
    public const double WarmP95ReferenceMilliseconds = 1.0;

    public static int Run()
    {
        try
        {
            SentenceUnanimityBundleReport report = Measure();
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

    internal static SentenceUnanimityBundleReport Measure()
    {
        IReadOnlyList<DecisionInventoryEntry> inventory =
            SentenceDecisionInventory.Build();
        SentenceUnanimityMorphologyResource morphology =
            SentenceUnanimityMorphology.Load();
        IReadOnlyList<DecisionEvaluationScenario> candidateScenarios =
            SentenceDecisionInventoryEvaluation.BuildScenarios(inventory);
        DecisionInventoryBaseline locativeBaseline =
            SentenceDecisionInventoryEvaluation.EvaluateBaseline(
                inventory,
                candidateScenarios);
        ImmutableArray<ImmutableArray<string>> ruleOrders =
            SentenceUnanimityBundleEvaluation.BuildRuleOrders();
        SentenceUnanimityBundleBaseline bundle =
            SentenceUnanimityBundleEvaluation.Evaluate(
                inventory,
                candidateScenarios,
                ruleOrders,
                locativeBaseline,
                morphology.Data);
        IReadOnlyList<SentenceUnanimityEvaluationScenario> jointScenarios =
            SentenceUnanimityBundleEvaluation.BuildJointScenarios(
                candidateScenarios,
                ruleOrders);

        int[] warmupSchedule = SentenceDecisionInventoryCommand.BuildSchedule(
            jointScenarios.Count,
            WarmupEvaluationCount,
            Seed ^ 0x5A17);
        int[] measuredSchedule = SentenceDecisionInventoryCommand.BuildSchedule(
            jointScenarios.Count,
            MeasuredEvaluationCount,
            Seed);
        for (int index = 0; index < WarmupEvaluationCount; index++)
        {
            SentenceUnanimityEvaluationScenario scenario =
                jointScenarios[warmupSchedule[index]];
            _ = SentenceUnanimityBundle.Evaluate(
                scenario.CandidateScenario,
                scenario.RuleOrder,
                morphology.Data);
        }

        var samples = new SentenceUnanimityTimingSample[MeasuredEvaluationCount];
        Process process = Process.GetCurrentProcess();
        process.Refresh();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long privateMemoryBefore = process.PrivateMemorySize64;
        long workingSetBefore = process.WorkingSet64;

        for (int sampleIndex = 0; sampleIndex < MeasuredEvaluationCount; sampleIndex++)
        {
            int jointScenarioIndex = measuredSchedule[sampleIndex];
            SentenceUnanimityEvaluationScenario scenario =
                jointScenarios[jointScenarioIndex];
            long started = Stopwatch.GetTimestamp();
            DecisionVerdict verdict = SentenceUnanimityBundle.Evaluate(
                scenario.CandidateScenario,
                scenario.RuleOrder,
                morphology.Data);
            long elapsed = Stopwatch.GetTimestamp() - started;
            samples[sampleIndex] = new SentenceUnanimityTimingSample(
                sampleIndex,
                scenario.CandidateScenario.PublicOrdinal,
                scenario.CandidateScenario.PermutationOrdinal,
                scenario.RuleOrderOrdinal,
                elapsed,
                SentenceDecisionInventoryEvaluation.VerdictName(verdict.Kind),
                verdict.CandidateIdentity);
        }

        long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        process.Refresh();
        TimeSpan cpuAfter = process.TotalProcessorTime;
        long privateMemoryAfter = process.PrivateMemorySize64;
        long workingSetAfter = process.WorkingSet64;
        long[] sampleTicks = samples.Select(static sample => sample.ElapsedTicks).ToArray();
        double p50 = NearestRankMilliseconds(sampleTicks, 0.50);
        double p95 = NearestRankMilliseconds(sampleTicks, 0.95);
        double p99 = NearestRankMilliseconds(sampleTicks, 0.99);
        double maximum = sampleTicks.Max() * 1000.0 / Stopwatch.Frequency;

        bool baselinePreserved = locativeBaseline.CorrectEditCount == 1
            && locativeBaseline.WrongEditCount == 0
            && locativeBaseline.AffirmativeKeepCount == 0
            && locativeBaseline.UsefulAbstentionCount == 19
            && locativeBaseline.RegrettableAbstentionCount == 15;
        SentenceUnanimityHypothesis hypothesis = new(
            ExistingBaselinePreserved: baselinePreserved,
            AtLeastTwoCorrectResidualEdits: bundle.CorrectResidualEditCount >= 2,
            AtLeastTwoResidualCandidateFamilies: bundle.CorrectResidualFamilyCount >= 2,
            ZeroWrongVisibleDecisions: bundle.WrongEditCount == 0,
            WarmP95BelowReference: p95 < WarmP95ReferenceMilliseconds);
        SentenceUnanimityValidity validity = new(
            PublicInventoryUnchanged: bundle.PublicCaseCount == 35
                && candidateScenarios.Count == 66,
            MorphologyArtifactCaptured: morphology.Report.Bytes > 0
                && morphology.Report.FormCount > 0
                && morphology.Report.SkippedLines == 0
                && morphology.Report.Sha256.Length == 64,
            FrozenRuleSetComplete: SentenceUnanimityBundle.FrozenRuleIds.Count == 5
                && SentenceUnanimityBundle.FrozenRuleOrder.Length == 5
                && SentenceUnanimityBundle.FrozenRuleOrder
                    .Distinct(StringComparer.Ordinal)
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(SentenceUnanimityBundle.FrozenRuleIds),
            EveryCandidateOrderExhaustive:
                locativeBaseline.AllCandidateOrdersExhaustive,
            EveryRuleOrderExhaustive: bundle.AllRuleOrdersExhaustive,
            EveryJointVerdictIdentityStable: bundle.AllJointVerdictsIdentityStable,
            EveryEditIdentifiesSubmittedCandidate: bundle.Cases
                .Where(static report => report.Verdict == "edit")
                .All(report => candidateScenarios
                    .Where(scenario => scenario.PublicOrdinal == report.PublicOrdinal)
                    .All(scenario => scenario.Candidates.Count(candidate =>
                        string.Equals(
                            candidate.Identity,
                            report.CandidateIdentity,
                            StringComparison.Ordinal)) == 1)),
            RawTimingSampleCountMatches: samples.Length == MeasuredEvaluationCount,
            MixedScheduleCoversEveryJointScenario: samples
                .Select(sample => (
                    sample.PublicOrdinal,
                    sample.CandidatePermutationOrdinal,
                    sample.RuleOrderOrdinal))
                .Distinct()
                .Count() == jointScenarios.Count,
            ResourceDeltasCaptured: allocatedAfter >= allocatedBefore
                && cpuAfter >= cpuBefore
                && privateMemoryBefore > 0
                && privateMemoryAfter > 0);

        return new SentenceUnanimityBundleReport(
            "ACX-0020",
            SentenceDecisionInventory.SourceCorpus,
            Seed,
            Stopwatch.Frequency,
            SentenceUnanimityBundle.FrozenRuleOrder,
            morphology.Report,
            locativeBaseline,
            bundle,
            hypothesis,
            new SentenceUnanimityTimingReport(
                WarmupEvaluationCount,
                samples.Length,
                WarmP95ReferenceMilliseconds,
                p50,
                p95,
                p99,
                maximum,
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
            "Public visible development corpus only. This run may establish frozen-rule behavior, exact submitted-edit selection, candidate/rule-order invariance, internal visible-development decisions, and warm in-process rule cost. It forbids grouped-validation, field-quality, applied-correction, UIA, injection, observed-target, end-to-end, physical-latency, production-safety, general precision, or GPU-work claims.");
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

internal sealed record SentenceUnanimityBundleReport(
    string ExperimentId,
    string Corpus,
    int Seed,
    long StopwatchFrequency,
    IReadOnlyList<string> FrozenRuleOrder,
    SentenceUnanimityMorphologyReport Morphology,
    DecisionInventoryBaseline LocativeBaseline,
    SentenceUnanimityBundleBaseline Bundle,
    SentenceUnanimityHypothesis Hypothesis,
    SentenceUnanimityTimingReport Timing,
    DecisionResourceReport Resources,
    SentenceUnanimityValidity Validity,
    string ClaimBoundary);

internal sealed record SentenceUnanimityHypothesis(
    bool ExistingBaselinePreserved,
    bool AtLeastTwoCorrectResidualEdits,
    bool AtLeastTwoResidualCandidateFamilies,
    bool ZeroWrongVisibleDecisions,
    bool WarmP95BelowReference)
{
    public bool Passed => ExistingBaselinePreserved
        && AtLeastTwoCorrectResidualEdits
        && AtLeastTwoResidualCandidateFamilies
        && ZeroWrongVisibleDecisions
        && WarmP95BelowReference;
}

internal sealed record SentenceUnanimityTimingReport(
    int WarmupEvaluationCount,
    int MeasuredEvaluationCount,
    double P95ReferenceMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaxMilliseconds,
    IReadOnlyList<SentenceUnanimityTimingSample> RawSamples);

internal readonly record struct SentenceUnanimityTimingSample(
    int SampleIndex,
    int PublicOrdinal,
    int CandidatePermutationOrdinal,
    int RuleOrderOrdinal,
    long ElapsedTicks,
    string Verdict,
    string? CandidateIdentity);

internal sealed record SentenceUnanimityValidity(
    bool PublicInventoryUnchanged,
    bool MorphologyArtifactCaptured,
    bool FrozenRuleSetComplete,
    bool EveryCandidateOrderExhaustive,
    bool EveryRuleOrderExhaustive,
    bool EveryJointVerdictIdentityStable,
    bool EveryEditIdentifiesSubmittedCandidate,
    bool RawTimingSampleCountMatches,
    bool MixedScheduleCoversEveryJointScenario,
    bool ResourceDeltasCaptured)
{
    public bool Valid => PublicInventoryUnchanged
        && MorphologyArtifactCaptured
        && FrozenRuleSetComplete
        && EveryCandidateOrderExhaustive
        && EveryRuleOrderExhaustive
        && EveryJointVerdictIdentityStable
        && EveryEditIdentifiesSubmittedCandidate
        && RawTimingSampleCountMatches
        && MixedScheduleCoversEveryJointScenario
        && ResourceDeltasCaptured;
}
