using System.Diagnostics;
using System.Text.Json;
using Deckle.Autocorrect.Onnx;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceCanonicalLatencyCommand
{
    public const int Rounds = 20;

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
            SentenceCanonicalLatencyReport report = Measure(parsed, model);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));
            bool invalid = IsError(report.FirstScore.Outcome)
                || report.Warmups.Any(static warmup => IsError(warmup.Outcome))
                || report.Trials.Any(static trial => IsError(trial.Outcome));
            return invalid ? 3 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    internal static IReadOnlyList<int> StrataForRound(int round) =>
        SentenceProfileFixture.StrataForRound(round);

    private static SentenceCanonicalLatencyReport Measure(
        ProbeArguments parsed,
        ModelSpec model)
    {
        Process process = Process.GetCurrentProcess();
        ProcessSnapshot beforeLoad = Snapshot(process);
        long loadStarted = Stopwatch.GetTimestamp();
        var scorer = new OnnxSentenceScorer(model.Directory, margin: 0.0, parsed.Provider);
        long loadTicks = Stopwatch.GetTimestamp() - loadStarted;
        ProcessSnapshot afterLoad = Snapshot(process);

        var warmups = new List<SentenceCanonicalLatencyWarmup>();
        var trials = new List<SentenceCanonicalLatencyTrial>();
        try
        {
            ProfileCandidateSet firstSet = CanonicalCandidates(2);
            TimedOutcome firstTimed = RunOrdinary(scorer, firstSet.Texts);
            var firstScore = new SentenceCanonicalLatencyFirstScore(
                firstTimed.ElapsedTicks,
                Summarize(firstTimed.Outcome));
            ProcessSnapshot afterFirstScore = Snapshot(process);

            foreach (int candidateCount in SentenceProfileFixture.CandidateCounts)
            {
                TimedOutcome timed = RunOrdinary(
                    scorer,
                    CanonicalCandidates(candidateCount).Texts);
                warmups.Add(new SentenceCanonicalLatencyWarmup(
                    candidateCount,
                    timed.ElapsedTicks,
                    Summarize(timed.Outcome)));
            }

            for (int round = 0; round < Rounds; round++)
            {
                IReadOnlyList<int> strata = StrataForRound(round);
                for (int position = 0; position < strata.Count; position++)
                {
                    int candidateCount = strata[position];
                    trials.Add(RunTrial(
                        scorer,
                        process,
                        round,
                        position,
                        CanonicalCandidates(candidateCount)));
                }
            }

            ProcessSnapshot afterTrials = Snapshot(process);
            return new SentenceCanonicalLatencyReport(
                model.Label,
                model.Directory,
                parsed.Provider,
                SentenceProfileFixture.Seed,
                Rounds,
                Stopwatch.Frequency,
                loadTicks,
                beforeLoad,
                afterLoad,
                afterFirstScore,
                afterTrials,
                firstScore,
                warmups,
                trials);
        }
        finally
        {
            scorer.Dispose();
        }
    }

    private static ProfileCandidateSet CanonicalCandidates(int candidateCount) =>
        SentenceProfileFixture.Candidates(candidateCount, rotation: 0);

    private static SentenceCanonicalLatencyTrial RunTrial(
        OnnxSentenceScorer scorer,
        Process process,
        int round,
        int position,
        ProfileCandidateSet set)
    {
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        TimeSpan cpuBefore = process.TotalProcessorTime;
        TimedOutcome timed = RunOrdinary(scorer, set.Texts);
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        long processCpuTicks = (process.TotalProcessorTime - cpuBefore).Ticks;
        process.Refresh();
        return new SentenceCanonicalLatencyTrial(
            round,
            position,
            set.Texts.Count,
            set.CanonicalIndices,
            timed.ElapsedTicks,
            allocatedBytes,
            processCpuTicks,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            Summarize(timed.Outcome));
    }

    private static TimedOutcome RunOrdinary(
        OnnxSentenceScorer scorer,
        IReadOnlyList<string> candidates)
    {
        long started = Stopwatch.GetTimestamp();
        SentenceScoringOutcome outcome = scorer.Score(candidates);
        return new TimedOutcome(outcome, Stopwatch.GetTimestamp() - started);
    }

    private static SentenceProfileOutcome Summarize(SentenceScoringOutcome outcome) => new(
        outcome.Chosen is null
            ? -1
            : outcome.Scores.ToList().FindIndex(score =>
                string.Equals(score.Text, outcome.Chosen, StringComparison.Ordinal)),
        outcome.Margin,
        outcome.Threshold,
        outcome.AbstainReason,
        outcome.Scores.Select(static score => score.Score).ToArray());

    private static bool IsError(SentenceProfileOutcome outcome) =>
        string.Equals(outcome.AbstainReason, "error", StringComparison.Ordinal);

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

internal sealed record SentenceCanonicalLatencyReport(
    string ModelLabel,
    string ModelDirectory,
    string Provider,
    int Seed,
    int Rounds,
    long StopwatchFrequency,
    long ModelLoadTicks,
    ProcessSnapshot BeforeLoad,
    ProcessSnapshot AfterLoad,
    ProcessSnapshot AfterFirstScore,
    ProcessSnapshot AfterTrials,
    SentenceCanonicalLatencyFirstScore FirstScore,
    IReadOnlyList<SentenceCanonicalLatencyWarmup> Warmups,
    IReadOnlyList<SentenceCanonicalLatencyTrial> Trials);

internal sealed record SentenceCanonicalLatencyFirstScore(
    long ElapsedTicks,
    SentenceProfileOutcome Outcome);

internal sealed record SentenceCanonicalLatencyWarmup(
    int CandidateCount,
    long ElapsedTicks,
    SentenceProfileOutcome Outcome);

internal sealed record SentenceCanonicalLatencyTrial(
    int Round,
    int Position,
    int CandidateCount,
    IReadOnlyList<int> CandidatePermutation,
    long ElapsedTicks,
    long ManagedAllocatedBytes,
    long ProcessCpuTicks,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    SentenceProfileOutcome Outcome);
