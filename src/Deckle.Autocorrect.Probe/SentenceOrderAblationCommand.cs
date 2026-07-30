using System.Diagnostics;
using System.Text.Json;
using Deckle.Autocorrect.Onnx;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceOrderAblationCommand
{
    private static readonly double[] StabilityThresholds = [0.0, 0.5, 1.0];

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
            SentenceOrderAblationReport report = Measure(parsed, model);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));

            bool invalid = report.Warmups.Any(static call =>
                    IsTechnicalFailure(call.Outcome))
                || report.QualityCalls.Any(static call =>
                    IsTechnicalFailure(call.Outcome))
                || report.LatencyCalls.Any(static call =>
                    IsTechnicalFailure(call.Outcome))
                || !report.RepeatedDecisionsStable;
            return invalid ? 3 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static SentenceOrderAblationReport Measure(
        ProbeArguments parsed,
        ModelSpec model)
    {
        Process process = Process.GetCurrentProcess();
        ProcessSnapshot beforeLoad = Snapshot(process);
        long loadStarted = Stopwatch.GetTimestamp();
        var scorer = new OnnxSentenceScorer(model.Directory, margin: 0.0, parsed.Provider);
        long loadTicks = Stopwatch.GetTimestamp() - loadStarted;
        ProcessSnapshot afterLoad = Snapshot(process);

        var warmups = new List<SentenceOrderAblationWarmup>();
        var qualityCalls = new List<SentenceOrderAblationQualityCall>();
        var latencyCalls = new List<SentenceOrderAblationLatencyCall>();
        try
        {
            ProfileCandidateSet latencySet = SentenceProfileFixture.Candidates(
                candidateCount: 2,
                rotation: 0);
            for (int cycle = 0;
                cycle < SentenceOrderAblationFixture.WarmupCycles;
                cycle++)
            {
                IReadOnlyList<SentenceOrderAblationMethod> methods =
                    SentenceOrderAblationFixture.QualityMethods(
                        caseIndex: 0,
                        repetition: cycle);
                for (int position = 0; position < methods.Count; position++)
                {
                    SentenceOrderAblationMethod method = methods[position];
                    TimedOutcome timed = RunTimed(scorer, latencySet.Texts, method);
                    warmups.Add(new SentenceOrderAblationWarmup(
                        cycle,
                        position,
                        MethodName(method),
                        timed.ElapsedTicks,
                        Summarize(timed.Outcome)));
                }
            }

            ProcessSnapshot afterWarmups = Snapshot(process);
            for (int caseIndex = 0;
                caseIndex < CorrectionBenchmarkCorpus.All.Count;
                caseIndex++)
            {
                CorrectionBenchmarkCase benchmarkCase =
                    CorrectionBenchmarkCorpus.All[caseIndex];
                int repetitions = SentenceOrderAblationFixture.QualityRepetitions(
                    benchmarkCase.Id);
                for (int repetition = 0; repetition < repetitions; repetition++)
                {
                    IReadOnlyList<SentenceOrderAblationMethod> methods =
                        SentenceOrderAblationFixture.QualityMethods(caseIndex, repetition);
                    for (int position = 0; position < methods.Count; position++)
                    {
                        SentenceOrderAblationMethod method = methods[position];
                        TimedOutcome timed = RunTimed(
                            scorer,
                            benchmarkCase.Candidates,
                            method);
                        qualityCalls.Add(new SentenceOrderAblationQualityCall(
                            benchmarkCase.Id,
                            benchmarkCase.Category,
                            caseIndex,
                            repetition,
                            position,
                            MethodName(method),
                            benchmarkCase.Candidates.Length,
                            benchmarkCase.LiteralIndex,
                            benchmarkCase.GoldIndex,
                            timed.ElapsedTicks,
                            Summarize(timed.Outcome)));
                    }
                }
            }

            bool repeatedDecisionsStable = RepeatedDecisionsAreStable(qualityCalls);
            ProcessSnapshot afterQuality = Snapshot(process);
            for (int block = 0;
                block < SentenceOrderAblationFixture.LatencyBlocks;
                block++)
            {
                IReadOnlyList<SentenceOrderAblationMethod> methods =
                    SentenceOrderAblationFixture.LatencyMethods(block);
                for (int position = 0; position < methods.Count; position++)
                {
                    latencyCalls.Add(RunLatencyCall(
                        scorer,
                        process,
                        latencySet,
                        block,
                        position,
                        methods[position]));
                }
            }

            ProcessSnapshot afterLatency = Snapshot(process);
            double forwardP95Milliseconds = NearestRankMilliseconds(
                latencyCalls
                    .Where(static call => call.Method == "forward")
                    .Select(static call => call.ElapsedTicks),
                percentile: 0.95);
            return new SentenceOrderAblationReport(
                model.Label,
                model.Directory,
                parsed.Provider,
                SentenceOrderAblationFixture.Seed,
                Stopwatch.Frequency,
                loadTicks,
                beforeLoad,
                afterLoad,
                afterWarmups,
                afterQuality,
                afterLatency,
                repeatedDecisionsStable,
                SentenceOrderAblationFixture.ContinuousHotForwardP95ReferenceMilliseconds,
                forwardP95Milliseconds,
                forwardP95Milliseconds <=
                    SentenceOrderAblationFixture
                        .ContinuousHotForwardP95ReferenceMilliseconds,
                warmups,
                qualityCalls,
                latencyCalls);
        }
        finally
        {
            scorer.Dispose();
        }
    }

    private static SentenceOrderAblationLatencyCall RunLatencyCall(
        OnnxSentenceScorer scorer,
        Process process,
        ProfileCandidateSet set,
        int block,
        int position,
        SentenceOrderAblationMethod method)
    {
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        TimeSpan cpuBefore = process.TotalProcessorTime;
        TimedOutcome timed = RunTimed(scorer, set.Texts, method);
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        long processCpuTicks = (process.TotalProcessorTime - cpuBefore).Ticks;
        process.Refresh();
        return new SentenceOrderAblationLatencyCall(
            block,
            position,
            MethodName(method),
            set.Texts.Count,
            set.CanonicalIndices,
            timed.ElapsedTicks,
            allocatedBytes,
            processCpuTicks,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            Summarize(timed.Outcome));
    }

    private static TimedOutcome RunTimed(
        OnnxSentenceScorer scorer,
        IReadOnlyList<string> candidates,
        SentenceOrderAblationMethod method)
    {
        long started = Stopwatch.GetTimestamp();
        SentenceScoringOutcome outcome = method switch
        {
            SentenceOrderAblationMethod.Forward => scorer.ScoreExperimental(
                candidates,
                OnnxSentenceScoringOrderMode.ForwardOnly),
            SentenceOrderAblationMethod.Reverse => scorer.ScoreExperimental(
                candidates,
                OnnxSentenceScoringOrderMode.ReverseOnly),
            SentenceOrderAblationMethod.Combined => scorer.Score(candidates),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
        return new TimedOutcome(outcome, Stopwatch.GetTimestamp() - started);
    }

    private static bool RepeatedDecisionsAreStable(
        IReadOnlyList<SentenceOrderAblationQualityCall> calls) =>
        calls.GroupBy(static call => (call.CaseId, call.Method))
            .Where(static group => group.Count() > 1)
            .All(group => StabilityThresholds.All(threshold =>
                group.Select(call => ThresholdDecision(call.Outcome, threshold))
                    .Distinct()
                    .Count() == 1));

    private static int ThresholdDecision(
        SentenceProfileOutcome outcome,
        double threshold) =>
        outcome.ChosenPresentedIndex >= 0
            && double.IsFinite(outcome.Margin)
            && outcome.Margin >= threshold
                ? outcome.ChosenPresentedIndex
                : -1;

    private static double NearestRankMilliseconds(
        IEnumerable<long> ticks,
        double percentile)
    {
        long[] sorted = ticks.Order().ToArray();
        if (sorted.Length == 0)
            throw new InvalidOperationException("No latency observations.");
        int index = Math.Max(0, (int)Math.Ceiling(sorted.Length * percentile) - 1);
        return sorted[index] * 1000.0 / Stopwatch.Frequency;
    }

    private static string MethodName(SentenceOrderAblationMethod method) =>
        method switch
        {
            SentenceOrderAblationMethod.Forward => "forward",
            SentenceOrderAblationMethod.Reverse => "reverse",
            SentenceOrderAblationMethod.Combined => "combined",
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

    private static SentenceProfileOutcome Summarize(SentenceScoringOutcome outcome) => new(
        outcome.Chosen is null
            ? -1
            : outcome.Scores.ToList().FindIndex(score =>
                string.Equals(score.Text, outcome.Chosen, StringComparison.Ordinal)),
        outcome.Margin,
        outcome.Threshold,
        outcome.AbstainReason,
        outcome.Scores.Select(static score => score.Score).ToArray());

    private static bool IsTechnicalFailure(SentenceProfileOutcome outcome) =>
        outcome.AbstainReason is not null
        && !string.Equals(
            outcome.AbstainReason,
            SentenceScoringOutcome.AbstainReasons.BelowMargin,
            StringComparison.Ordinal);

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

    private sealed record TimedOutcome(
        SentenceScoringOutcome Outcome,
        long ElapsedTicks);
}

internal sealed record SentenceOrderAblationReport(
    string ModelLabel,
    string ModelDirectory,
    string Provider,
    int Seed,
    long StopwatchFrequency,
    long ModelLoadTicks,
    ProcessSnapshot BeforeLoad,
    ProcessSnapshot AfterLoad,
    ProcessSnapshot AfterWarmups,
    ProcessSnapshot AfterQuality,
    ProcessSnapshot AfterLatency,
    bool RepeatedDecisionsStable,
    double ContinuousHotForwardP95ReferenceMilliseconds,
    double ContinuousHotForwardP95Milliseconds,
    bool ContinuousHotForwardAtOrBelowReference,
    IReadOnlyList<SentenceOrderAblationWarmup> Warmups,
    IReadOnlyList<SentenceOrderAblationQualityCall> QualityCalls,
    IReadOnlyList<SentenceOrderAblationLatencyCall> LatencyCalls);

internal sealed record SentenceOrderAblationWarmup(
    int Cycle,
    int Position,
    string Method,
    long ElapsedTicks,
    SentenceProfileOutcome Outcome);

internal sealed record SentenceOrderAblationQualityCall(
    string CaseId,
    string Category,
    int CaseIndex,
    int Repetition,
    int Position,
    string Method,
    int CandidateCount,
    int LiteralIndex,
    int GoldIndex,
    long ElapsedTicks,
    SentenceProfileOutcome Outcome);

internal sealed record SentenceOrderAblationLatencyCall(
    int Block,
    int Position,
    string Method,
    int CandidateCount,
    IReadOnlyList<int> CandidatePermutation,
    long ElapsedTicks,
    long ManagedAllocatedBytes,
    long ProcessCpuTicks,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    SentenceProfileOutcome Outcome);
