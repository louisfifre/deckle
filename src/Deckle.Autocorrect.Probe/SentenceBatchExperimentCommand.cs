using System.Diagnostics;
using System.Text.Json;
using Deckle.Autocorrect.Onnx;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceBatchExperimentCommand
{
    private static readonly double[] StabilityThresholds = [0.0, 0.5, 1.0];
    private const double ScoreTolerance = 1e-3;

    public static int Run(ProbeArguments parsed)
    {
        ModelSpec model = parsed.Models[0];
        if (!Directory.Exists(model.Directory))
        {
            Console.Error.WriteLine($"Missing model directory: {model.Directory}");
            return 1;
        }

        try
        {
            SentenceBatchExperimentReport report = Measure(parsed, model);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));
            return report.TechnicallyValid ? 0 : 3;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static SentenceBatchExperimentReport Measure(
        ProbeArguments parsed,
        ModelSpec model)
    {
        Process process = Process.GetCurrentProcess();
        ProcessSnapshot beforeLoad = Snapshot(process);
        long loadStarted = Stopwatch.GetTimestamp();
        using var scorer = new OnnxSentenceScorer(
            model.Directory,
            margin: 0.0,
            parsed.Provider);
        long loadTicks = Stopwatch.GetTimestamp() - loadStarted;
        ProcessSnapshot afterLoad = Snapshot(process);

        ProfileCandidateSet canonical = SentenceProfileFixture.Candidates(
            candidateCount: 2,
            rotation: 0);
        var warmups = new List<SentenceBatchExperimentCall>();
        var warmupEquivalence = new List<bool>();
        for (int pair = 0; pair < SentenceBatchExperimentFixture.WarmupPairs; pair++)
        {
            IReadOnlyList<string> presentation =
                SentenceBatchExperimentFixture.WarmupUsesReversedPresentation(pair)
                    ? canonical.Texts.Reverse().ToArray()
                    : canonical.Texts;
            IReadOnlyList<SentenceBatchExperimentMethod> methods =
                SentenceBatchExperimentFixture.WarmupMethods(pair);
            var pairCalls = new List<SentenceBatchExperimentCall>(methods.Count);
            for (int position = 0; position < methods.Count; position++)
            {
                SentenceBatchExperimentCall call = RunCall(
                    scorer,
                    presentation,
                    phase: "warmup",
                    block: pair,
                    position,
                    methods[position]);
                warmups.Add(call);
                pairCalls.Add(call);
            }
            warmupEquivalence.Add(CallsEquivalent(pairCalls));
        }

        ProcessSnapshot afterWarmups = Snapshot(process);
        var latencyCalls = new List<SentenceBatchExperimentCall>();
        var blockReports = new List<SentenceBatchExperimentBlock>();
        for (int block = 0;
            block < SentenceBatchExperimentFixture.LatencyBlocks;
            block++)
        {
            IReadOnlyList<SentenceBatchExperimentMethod> methods =
                SentenceBatchExperimentFixture.LatencyMethods(block);
            var blockCalls = new List<SentenceBatchExperimentCall>(methods.Count);
            for (int position = 0; position < methods.Count; position++)
            {
                SentenceBatchExperimentCall call = RunCall(
                    scorer,
                    canonical.Texts,
                    phase: "latency",
                    block,
                    position,
                    methods[position]);
                latencyCalls.Add(call);
                blockCalls.Add(call);
            }

            blockReports.Add(BuildBlockReport(block, blockCalls));
        }

        ProcessSnapshot afterLatency = Snapshot(process);
        SentenceBatchCombinedControl combinedControl = RunCombinedControl(
            scorer,
            canonical.Texts);
        ProcessSnapshot afterControl = Snapshot(process);

        bool callsTechnicallyValid = warmups.Concat(latencyCalls)
            .All(static call => call.TechnicallyValid);
        bool scheduleComplete = warmups.Count == 4
            && latencyCalls.Count == 20
            && blockReports.All(static block => block.Complete);
        bool warmupsEquivalent = warmupEquivalence.All(static value => value);
        bool measuredEquivalent = blockReports.All(static block => block.Equivalent);
        double medianBlockRatioValue = Median(
            blockReports.Select(static block =>
                block.BatchToSequentialRatio ?? double.NaN));
        double? medianBlockRatio = double.IsFinite(medianBlockRatioValue)
            ? medianBlockRatioValue
            : null;
        int fasterBlocks = blockReports.Count(static block => block.BatchFaster);
        double batchMedianMillisecondsValue = TicksToMilliseconds(Median(
            latencyCalls
                .Where(static call => call.Method == "batch")
                .Select(static call => (double)call.ElapsedTicks)));
        bool technicallyValid = callsTechnicallyValid
            && scheduleComplete
            && combinedControl.TechnicallyValid;
        bool semanticallyEquivalent = warmupsEquivalent
            && measuredEquivalent
            && combinedControl.Equivalent;
        bool latencyHypothesisPassed = LatencyHypothesisPasses(
            technicallyValid,
            warmupsEquivalent,
            measuredEquivalent,
            combinedControl.Equivalent,
            medianBlockRatio,
            fasterBlocks);
        double? batchMedianMilliseconds = technicallyValid
            && semanticallyEquivalent
            && double.IsFinite(batchMedianMillisecondsValue)
                ? batchMedianMillisecondsValue
                : null;
        bool? batchMedianAtOrBelowSecondaryReference =
            batchMedianMilliseconds is double batchMilliseconds
                ? batchMilliseconds
                    <= SentenceBatchExperimentFixture.SecondaryLatencyReferenceMilliseconds
                : null;

        return new SentenceBatchExperimentReport(
            model.Label,
            model.Directory,
            parsed.Provider,
            Stopwatch.Frequency,
            loadTicks,
            beforeLoad,
            afterLoad,
            afterWarmups,
            afterLatency,
            afterControl,
            technicallyValid,
            scheduleComplete,
            warmupsEquivalent,
            measuredEquivalent,
            semanticallyEquivalent,
            combinedControl,
            ScoreTolerance,
            StabilityThresholds,
            SentenceBatchExperimentFixture.MaximumMedianBlockRatio,
            SentenceBatchExperimentFixture.MinimumFasterBlocks,
            medianBlockRatio,
            fasterBlocks,
            latencyHypothesisPassed,
            SentenceBatchExperimentFixture.SecondaryLatencyReferenceMilliseconds,
            batchMedianMilliseconds,
            batchMedianAtOrBelowSecondaryReference,
            warmups,
            latencyCalls,
            blockReports);
    }

    private static SentenceBatchExperimentCall RunCall(
        OnnxSentenceScorer scorer,
        IReadOnlyList<string> candidates,
        string phase,
        int block,
        int position,
        SentenceBatchExperimentMethod method)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            if (method == SentenceBatchExperimentMethod.Sequential)
            {
                SentenceScoringOutcome sequentialOutcome = scorer.ScoreExperimental(
                    candidates,
                    OnnxSentenceScoringOrderMode.ForwardOnly);
                return new SentenceBatchExperimentCall(
                    phase,
                    block,
                    position,
                    "sequential",
                    Stopwatch.GetTimestamp() - started,
                    !IsTechnicalFailure(sequentialOutcome),
                    null,
                    null,
                    0,
                    0,
                    Array.Empty<long>(),
                    null,
                    Summarize(sequentialOutcome));
            }

            SentenceBatchExperimentOutcome batch = scorer.ScoreBatchExperimental(candidates);
            return new SentenceBatchExperimentCall(
                phase,
                block,
                position,
                "batch",
                Stopwatch.GetTimestamp() - started,
                batch.TechnicallyValid,
                batch.FailureStage,
                batch.FailureReason,
                batch.PromptTokens,
                batch.SequenceLength,
                batch.TensorShape,
                batch.TensorType,
                batch.Outcome is SentenceScoringOutcome batchOutcome
                    ? Summarize(batchOutcome)
                    : null);
        }
        catch (Exception error)
        {
            return new SentenceBatchExperimentCall(
                phase,
                block,
                position,
                MethodName(method),
                Stopwatch.GetTimestamp() - started,
                false,
                "native_call",
                error.GetType().Name,
                0,
                0,
                Array.Empty<long>(),
                null,
                null);
        }
    }

    private static SentenceBatchExperimentBlock BuildBlockReport(
        int block,
        IReadOnlyList<SentenceBatchExperimentCall> calls)
    {
        SentenceBatchExperimentCall[] sequential = calls
            .Where(static call => call.Method == "sequential")
            .ToArray();
        SentenceBatchExperimentCall[] batch = calls
            .Where(static call => call.Method == "batch")
            .ToArray();
        bool complete = calls.Count == 4
            && sequential.Length == 2
            && batch.Length == 2
            && calls.All(static call => call.TechnicallyValid);
        double? sequentialMedian = complete
            ? Median(sequential.Select(static call => (double)call.ElapsedTicks))
            : null;
        double? batchMedian = complete
            ? Median(batch.Select(static call => (double)call.ElapsedTicks))
            : null;
        double? ratio = null;
        bool batchFaster = false;
        if (complete
            && sequentialMedian is double sequentialTicks
            && batchMedian is double batchTicks
            && sequentialTicks > 0.0)
        {
            ratio = batchTicks / sequentialTicks;
            batchFaster = batchTicks < sequentialTicks;
        }
        bool equivalent = complete && sequential.All(reference =>
            batch.All(candidate => OutcomesEquivalent(reference.Outcome, candidate.Outcome)));
        return new SentenceBatchExperimentBlock(
            block,
            complete,
            equivalent,
            sequentialMedian,
            batchMedian,
            ratio,
            batchFaster);
    }

    private static SentenceBatchCombinedControl RunCombinedControl(
        OnnxSentenceScorer scorer,
        IReadOnlyList<string> candidates)
    {
        try
        {
            SentenceBatchExperimentOutcome forward = scorer.ScoreBatchExperimental(candidates);
            string[] reversedCandidates = candidates.Reverse().ToArray();
            SentenceBatchExperimentOutcome reverse = scorer.ScoreBatchExperimental(
                reversedCandidates);
            SentenceScoringOutcome sequentialCombined = scorer.Score(candidates);
            if (!forward.TechnicallyValid || !reverse.TechnicallyValid
                || forward.Outcome is not SentenceScoringOutcome forwardOutcome
                || reverse.Outcome is not SentenceScoringOutcome reverseOutcome
                || IsTechnicalFailure(sequentialCombined))
            {
                return new SentenceBatchCombinedControl(
                    false,
                    false,
                    forward.FailureStage ?? reverse.FailureStage ?? "sequential_combined",
                    forward.FailureReason ?? reverse.FailureReason
                        ?? sequentialCombined.AbstainReason);
            }

            SentenceScoringOutcome batchedCombined = CombineOrders(
                candidates,
                forwardOutcome,
                reverseOutcome);
            return new SentenceBatchCombinedControl(
                true,
                OutcomesEquivalent(
                    Summarize(sequentialCombined),
                    Summarize(batchedCombined)),
                null,
                null);
        }
        catch (Exception error)
        {
            return new SentenceBatchCombinedControl(
                false,
                false,
                "native_call",
                error.GetType().Name);
        }
    }

    private static SentenceScoringOutcome CombineOrders(
        IReadOnlyList<string> candidates,
        SentenceScoringOutcome forward,
        SentenceScoringOutcome reverse)
    {
        var scores = new SentenceCandidateScore[2];
        for (int index = 0; index < 2; index++)
        {
            SentenceCandidateScore left = forward.Scores[index];
            SentenceCandidateScore right = reverse.Scores[1 - index];
            scores[index] = new SentenceCandidateScore(
                candidates[index],
                (left.Score + right.Score) / 2.0,
                (left.LogProbability + right.LogProbability) / 2.0,
                Math.Max(left.ScoredTokenCount, right.ScoredTokenCount));
        }

        int best = scores[1].Score > scores[0].Score ? 1 : 0;
        double margin = scores[best].Score - scores[1 - best].Score;
        bool cleared = double.IsFinite(margin) && margin > 0.0;
        return new SentenceScoringOutcome(
            cleared ? scores[best].Text : null,
            scores,
            margin,
            0.0,
            cleared ? null : SentenceScoringOutcome.AbstainReasons.BelowMargin);
    }

    private static bool CallsEquivalent(
        IReadOnlyList<SentenceBatchExperimentCall> calls)
    {
        SentenceBatchExperimentCall? sequential = calls.SingleOrDefault(
            static call => call.Method == "sequential");
        SentenceBatchExperimentCall? batch = calls.SingleOrDefault(
            static call => call.Method == "batch");
        return sequential is not null
            && batch is not null
            && sequential.TechnicallyValid
            && batch.TechnicallyValid
            && OutcomesEquivalent(sequential.Outcome, batch.Outcome);
    }

    internal static bool OutcomesEquivalent(
        SentenceBatchOutcomeSummary? left,
        SentenceBatchOutcomeSummary? right)
    {
        if (left is null || right is null
            || left.Scores.Count != right.Scores.Count
            || left.LogProbabilities.Count != right.LogProbabilities.Count
            || left.ScoredTokenCounts.Count != right.ScoredTokenCounts.Count
            || left.Scores.Count != left.LogProbabilities.Count
            || left.Scores.Count != left.ScoredTokenCounts.Count
            || right.Scores.Count != right.LogProbabilities.Count
            || right.Scores.Count != right.ScoredTokenCounts.Count
            || left.ChosenPresentedIndex != right.ChosenPresentedIndex
            || !string.Equals(
                left.AbstainReason,
                right.AbstainReason,
                StringComparison.Ordinal)
            || !double.IsFinite(left.Margin)
            || !double.IsFinite(right.Margin)
            || !double.IsFinite(left.Threshold)
            || !double.IsFinite(right.Threshold)
            || Math.Abs(left.Margin - right.Margin) > ScoreTolerance
            || Math.Abs(left.Threshold - right.Threshold) > ScoreTolerance)
        {
            return false;
        }

        for (int index = 0; index < left.Scores.Count; index++)
        {
            if (!double.IsFinite(left.Scores[index])
                || !double.IsFinite(right.Scores[index])
                || !double.IsFinite(left.LogProbabilities[index])
                || !double.IsFinite(right.LogProbabilities[index]))
            {
                return false;
            }
            if (Math.Abs(left.Scores[index] - right.Scores[index]) > ScoreTolerance)
                return false;
            if (left.ScoredTokenCounts[index] != right.ScoredTokenCounts[index])
                return false;
            double logProbabilityTolerance = ScoreTolerance
                * Math.Max(1, left.ScoredTokenCounts[index]);
            if (Math.Abs(
                    left.LogProbabilities[index]
                    - right.LogProbabilities[index]) > logProbabilityTolerance)
            {
                return false;
            }
        }

        return StabilityThresholds.All(threshold =>
            ThresholdDecision(left, threshold) == ThresholdDecision(right, threshold));
    }

    internal static bool LatencyHypothesisPasses(
        bool technicallyValid,
        bool warmupsEquivalent,
        bool measuredEquivalent,
        bool combinedEquivalent,
        double? medianBlockRatio,
        int fasterBlocks) =>
        technicallyValid
        && warmupsEquivalent
        && measuredEquivalent
        && combinedEquivalent
        && medianBlockRatio is double ratio
        && double.IsFinite(ratio)
        && ratio <= SentenceBatchExperimentFixture.MaximumMedianBlockRatio
        && fasterBlocks >= SentenceBatchExperimentFixture.MinimumFasterBlocks;

    private static int ThresholdDecision(
        SentenceBatchOutcomeSummary outcome,
        double threshold) =>
        outcome.ChosenPresentedIndex >= 0
            && double.IsFinite(outcome.Margin)
            && outcome.Margin >= threshold
                ? outcome.ChosenPresentedIndex
                : -1;

    private static bool IsTechnicalFailure(SentenceScoringOutcome outcome) =>
        outcome.AbstainReason is not null
        && !string.Equals(
            outcome.AbstainReason,
            SentenceScoringOutcome.AbstainReasons.BelowMargin,
            StringComparison.Ordinal);

    private static SentenceBatchOutcomeSummary Summarize(
        SentenceScoringOutcome outcome) => new(
        outcome.Chosen is null
            ? -1
            : outcome.Scores.ToList().FindIndex(score =>
                string.Equals(score.Text, outcome.Chosen, StringComparison.Ordinal)),
        outcome.Margin,
        outcome.Threshold,
        outcome.AbstainReason,
        outcome.Scores.Select(static score => score.Score).ToArray(),
        outcome.Scores.Select(static score => score.LogProbability).ToArray(),
        outcome.Scores.Select(static score => score.ScoredTokenCount).ToArray());

    private static string MethodName(SentenceBatchExperimentMethod method) =>
        method == SentenceBatchExperimentMethod.Batch ? "batch" : "sequential";

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.Where(double.IsFinite).Order().ToArray();
        if (sorted.Length == 0)
            return double.NaN;
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2.0
            : sorted[middle];
    }

    private static double TicksToMilliseconds(double ticks) =>
        ticks * 1000.0 / Stopwatch.Frequency;

    private static ProcessSnapshot Snapshot(Process process)
    {
        process.Refresh();
        return new ProcessSnapshot(
            process.TotalProcessorTime.Ticks,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.PeakWorkingSet64,
            GC.GetTotalAllocatedBytes(precise: true),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }
}

internal sealed record SentenceBatchExperimentReport(
    string ModelLabel,
    string ModelDirectory,
    string Provider,
    long StopwatchFrequency,
    long ModelLoadTicks,
    ProcessSnapshot BeforeLoad,
    ProcessSnapshot AfterLoad,
    ProcessSnapshot AfterWarmups,
    ProcessSnapshot AfterLatency,
    ProcessSnapshot AfterControl,
    bool TechnicallyValid,
    bool ScheduleComplete,
    bool WarmupsEquivalent,
    bool MeasuredEquivalent,
    bool SemanticallyEquivalent,
    SentenceBatchCombinedControl CombinedControl,
    double ScoreTolerance,
    IReadOnlyList<double> StabilityThresholds,
    double MaximumMedianBlockRatio,
    int MinimumFasterBlocks,
    double? MedianBlockRatio,
    int FasterBlocks,
    bool LatencyHypothesisPassed,
    double SecondaryLatencyReferenceMilliseconds,
    double? BatchMedianMilliseconds,
    bool? BatchMedianAtOrBelowSecondaryReference,
    IReadOnlyList<SentenceBatchExperimentCall> Warmups,
    IReadOnlyList<SentenceBatchExperimentCall> LatencyCalls,
    IReadOnlyList<SentenceBatchExperimentBlock> Blocks);

internal sealed record SentenceBatchExperimentCall(
    string Phase,
    int Block,
    int Position,
    string Method,
    long ElapsedTicks,
    bool TechnicallyValid,
    string? FailureStage,
    string? FailureReason,
    int PromptTokens,
    int SequenceLength,
    IReadOnlyList<long> TensorShape,
    string? TensorType,
    SentenceBatchOutcomeSummary? Outcome);

internal sealed record SentenceBatchOutcomeSummary(
    int ChosenPresentedIndex,
    double Margin,
    double Threshold,
    string? AbstainReason,
    IReadOnlyList<double> Scores,
    IReadOnlyList<double> LogProbabilities,
    IReadOnlyList<int> ScoredTokenCounts);

internal sealed record SentenceBatchExperimentBlock(
    int Block,
    bool Complete,
    bool Equivalent,
    double? SequentialMedianTicks,
    double? BatchMedianTicks,
    double? BatchToSequentialRatio,
    bool BatchFaster);

internal sealed record SentenceBatchCombinedControl(
    bool TechnicallyValid,
    bool Equivalent,
    string? FailureStage,
    string? FailureReason);
