using System.Diagnostics;
using System.Text.Json;
using Deckle.Autocorrect.Onnx;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceCalibrationCommand
{
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
            SentenceCalibrationReport report = RunCalibration(parsed, model);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));

            bool invalid = IsError(report.FirstScore.Outcome)
                || report.Warmups.Any(static warmup => IsError(warmup.Outcome))
                || report.OrdinaryTrials.Any(static trial => IsError(trial.Outcome))
                || report.CalibrationBlocks.Any(static block =>
                    !block.EquivalentOutcome
                    || block.Calls.Any(static call => IsError(call.Outcome)));
            return invalid ? 3 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static SentenceCalibrationReport RunCalibration(
        ProbeArguments parsed,
        ModelSpec model)
    {
        Process process = Process.GetCurrentProcess();
        ProcessSnapshot beforeLoad = Snapshot(process);
        long loadStarted = Stopwatch.GetTimestamp();
        var scorer = new OnnxSentenceScorer(model.Directory, margin: 0.0, parsed.Provider);
        long loadTicks = Stopwatch.GetTimestamp() - loadStarted;
        ProcessSnapshot afterLoad = Snapshot(process);

        var warmups = new List<SentenceCalibrationWarmup>();
        var ordinaryTrials = new List<SentenceCalibrationOrdinaryTrial>();
        var calibrationBlocks = new List<SentenceCalibrationBlock>();
        try
        {
            ProfileCandidateSet firstSet = SentenceProfileFixture.Candidates(2, rotation: 0);
            TimedOutcome firstTimed = RunOrdinary(scorer, firstSet.Texts);
            var firstScore = new SentenceCalibrationFirstScore(
                firstTimed.ElapsedTicks,
                Summarize(firstTimed.Outcome));
            ProcessSnapshot afterFirstScore = Snapshot(process);

            foreach (int candidateCount in SentenceProfileFixture.CandidateCounts)
            {
                ProfileCandidateSet set = SentenceProfileFixture.Candidates(
                    candidateCount,
                    rotation: 0);
                TimedOutcome timed = RunOrdinary(scorer, set.Texts);
                warmups.Add(new SentenceCalibrationWarmup(
                    candidateCount,
                    timed.ElapsedTicks,
                    Summarize(timed.Outcome)));
            }

            for (int round = 0; round < SentenceCalibrationFixture.OrdinaryRounds; round++)
            {
                IReadOnlyList<int> strata =
                    SentenceCalibrationFixture.OrdinaryStrataForRound(round);
                for (int position = 0; position < strata.Count; position++)
                {
                    int candidateCount = strata[position];
                    ProfileCandidateSet set = SentenceProfileFixture.Candidates(
                        candidateCount,
                        SentenceCalibrationFixture.OrdinaryRotation(round, candidateCount));
                    ordinaryTrials.Add(RunOrdinaryTrial(
                        scorer,
                        process,
                        round,
                        position,
                        set));
                }
            }

            ProcessSnapshot afterOrdinary = Snapshot(process);

            for (int block = 0;
                block < SentenceCalibrationFixture.CalibrationBlocksPerStratum;
                block++)
            {
                foreach (int candidateCount in
                    SentenceCalibrationFixture.CalibrationCandidateCounts)
                {
                    ProfileCandidateSet set = SentenceProfileFixture.Candidates(
                        candidateCount,
                        SentenceCalibrationFixture.CalibrationRotation(
                            block,
                            candidateCount));
                    calibrationBlocks.Add(RunCalibrationBlock(
                        scorer,
                        block,
                        set));
                }
            }

            ProcessSnapshot afterCalibration = Snapshot(process);
            return new SentenceCalibrationReport(
                model.Label,
                model.Directory,
                parsed.Provider,
                SentenceProfileFixture.Seed,
                SentenceCalibrationFixture.OrdinaryRounds,
                SentenceCalibrationFixture.CalibrationBlocksPerStratum,
                SentenceCalibrationFixture.CalibrationCandidateCounts,
                Stopwatch.Frequency,
                loadTicks,
                beforeLoad,
                afterLoad,
                afterFirstScore,
                afterOrdinary,
                afterCalibration,
                firstScore,
                warmups,
                ordinaryTrials,
                calibrationBlocks);
        }
        finally
        {
            scorer.Dispose();
        }
    }

    private static SentenceCalibrationOrdinaryTrial RunOrdinaryTrial(
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
        return new SentenceCalibrationOrdinaryTrial(
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

    private static SentenceCalibrationBlock RunCalibrationBlock(
        OnnxSentenceScorer scorer,
        int block,
        ProfileCandidateSet set)
    {
        IReadOnlyList<SentenceCalibrationMethod> methods =
            SentenceCalibrationFixture.MethodsForBlock(block);
        var calls = new List<SentenceCalibrationCall>(methods.Count);
        var outcomes = new List<SentenceScoringOutcome>(methods.Count);
        for (int callPosition = 0; callPosition < methods.Count; callPosition++)
        {
            SentenceCalibrationMethod method = methods[callPosition];
            TimedOutcome timed = method == SentenceCalibrationMethod.Profiled
                ? RunProfiled(scorer, set.Texts)
                : RunOrdinary(scorer, set.Texts);
            outcomes.Add(timed.Outcome);
            calls.Add(new SentenceCalibrationCall(
                callPosition,
                method.ToString().ToLowerInvariant(),
                timed.ElapsedTicks,
                Summarize(timed.Outcome),
                timed.Profile));
        }

        bool equivalent = outcomes.Skip(1).All(outcome =>
            SentenceProfileCommand.OutcomesAreExactlyEquivalent(outcomes[0], outcome));
        return new SentenceCalibrationBlock(
            block,
            set.Texts.Count,
            set.CanonicalIndices,
            SentenceCalibrationFixture.IsProfiledOuter(block)
                ? "profiled_ordinary_ordinary_profiled"
                : "ordinary_profiled_profiled_ordinary",
            equivalent,
            calls);
    }

    private static TimedOutcome RunProfiled(
        OnnxSentenceScorer scorer,
        IReadOnlyList<string> candidates)
    {
        long started = Stopwatch.GetTimestamp();
        ProfiledSentenceScoringOutcome result = scorer.ScoreProfiled(candidates);
        return new TimedOutcome(
            result.Outcome,
            Stopwatch.GetTimestamp() - started,
            result.Profile);
    }

    private static TimedOutcome RunOrdinary(
        OnnxSentenceScorer scorer,
        IReadOnlyList<string> candidates)
    {
        long started = Stopwatch.GetTimestamp();
        SentenceScoringOutcome outcome = scorer.Score(candidates);
        return new TimedOutcome(outcome, Stopwatch.GetTimestamp() - started, null);
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
        long ElapsedTicks,
        OnnxSentenceScoringProfile? Profile);
}

internal sealed record SentenceCalibrationReport(
    string ModelLabel,
    string ModelDirectory,
    string Provider,
    int Seed,
    int OrdinaryRounds,
    int CalibrationBlocksPerStratum,
    IReadOnlyList<int> CalibrationCandidateCounts,
    long StopwatchFrequency,
    long ModelLoadTicks,
    ProcessSnapshot BeforeLoad,
    ProcessSnapshot AfterLoad,
    ProcessSnapshot AfterFirstScore,
    ProcessSnapshot AfterOrdinary,
    ProcessSnapshot AfterCalibration,
    SentenceCalibrationFirstScore FirstScore,
    IReadOnlyList<SentenceCalibrationWarmup> Warmups,
    IReadOnlyList<SentenceCalibrationOrdinaryTrial> OrdinaryTrials,
    IReadOnlyList<SentenceCalibrationBlock> CalibrationBlocks);

internal sealed record SentenceCalibrationFirstScore(
    long ElapsedTicks,
    SentenceProfileOutcome Outcome);

internal sealed record SentenceCalibrationWarmup(
    int CandidateCount,
    long ElapsedTicks,
    SentenceProfileOutcome Outcome);

internal sealed record SentenceCalibrationOrdinaryTrial(
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

internal sealed record SentenceCalibrationBlock(
    int Block,
    int CandidateCount,
    IReadOnlyList<int> CandidatePermutation,
    string Sequence,
    bool EquivalentOutcome,
    IReadOnlyList<SentenceCalibrationCall> Calls);

internal sealed record SentenceCalibrationCall(
    int CallPosition,
    string Method,
    long ElapsedTicks,
    SentenceProfileOutcome Outcome,
    OnnxSentenceScoringProfile? Profile);
