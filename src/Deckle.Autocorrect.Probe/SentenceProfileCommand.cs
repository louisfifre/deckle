using System.Diagnostics;
using System.Text.Json;
using Deckle.Autocorrect.Onnx;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceProfileCommand
{
    private const int OverheadPairs = 5;

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
            SentenceProfileReport report = RunProfile(parsed, model);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));
            bool hasScoringError = report.Trials.Any(static trial => IsError(trial.Outcome))
                || report.Warmups.Any(static warmup => IsError(warmup.Outcome))
                || report.OverheadPairs.Any(static pair =>
                    string.Equals(pair.ProfiledAbstainReason, "error", StringComparison.Ordinal)
                    || string.Equals(pair.OrdinaryAbstainReason, "error", StringComparison.Ordinal)
                    || !pair.EquivalentOutcome)
                || report.QualityCases.Any(static quality =>
                    string.Equals(quality.AbstainReason, "error", StringComparison.Ordinal)
                    || !quality.EquivalentOutcome)
                || report.EquivalenceChecks.Any(static check =>
                    IsError(check.ProfiledOutcome)
                    || IsError(check.OrdinaryOutcome)
                    || !check.EquivalentOutcome);
            return hasScoringError ? 3 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static SentenceProfileReport RunProfile(ProbeArguments parsed, ModelSpec model)
    {
        Process process = Process.GetCurrentProcess();
        ProcessSnapshot beforeLoad = Snapshot(process);
        long loadStarted = Stopwatch.GetTimestamp();
        var scorer = new OnnxSentenceScorer(model.Directory, margin: 0.0, parsed.Provider);
        long loadTicks = Stopwatch.GetTimestamp() - loadStarted;
        ProcessSnapshot afterLoad = Snapshot(process);

        var trials = new List<SentenceProfileTrial>();
        var warmups = new List<SentenceProfileWarmup>();
        var overheadPairs = new List<SentenceProfileOverheadPair>();
        var equivalenceChecks = new List<SentenceProfileEquivalenceCheck>();
        var quality = new List<SentenceProfileQualityCase>();
        try
        {
            ProfileCandidateSet firstSet = SentenceProfileFixture.Candidates(2, rotation: 0);
            trials.Add(RunProfiledTrial(
                scorer, process, "model_loaded_first_score", round: -1, firstSet));
            ProcessSnapshot afterFirstScore = Snapshot(process);

            foreach (int candidateCount in SentenceProfileFixture.CandidateCounts)
            {
                ProfileCandidateSet set = SentenceProfileFixture.Candidates(candidateCount, rotation: 0);
                warmups.Add(RunWarmup(scorer, set));
            }

            for (int pair = 0; pair < OverheadPairs; pair++)
            {
                ProfileCandidateSet set = SentenceProfileFixture.Candidates(2, rotation: pair);
                bool profiledFirst = pair % 2 == 0;
                TimedOutcome profiled;
                TimedOutcome ordinary;
                if (profiledFirst)
                {
                    profiled = RunProfiled(scorer, set.Texts);
                    ordinary = RunOrdinary(scorer, set.Texts);
                }
                else
                {
                    ordinary = RunOrdinary(scorer, set.Texts);
                    profiled = RunProfiled(scorer, set.Texts);
                }

                overheadPairs.Add(new SentenceProfileOverheadPair(
                    pair,
                    profiledFirst,
                    profiled.ElapsedTicks,
                    ordinary.ElapsedTicks,
                    profiled.Outcome.AbstainReason,
                    ordinary.Outcome.AbstainReason,
                    OutcomesAreExactlyEquivalent(profiled.Outcome, ordinary.Outcome)));
            }

            for (int round = 0; round < parsed.Iterations; round++)
            {
                IReadOnlyList<int> strata = SentenceProfileFixture.StrataForRound(round);
                for (int position = 0; position < strata.Count; position++)
                {
                    int candidateCount = strata[position];
                    ProfileCandidateSet set = SentenceProfileFixture.Candidates(
                        candidateCount,
                        SentenceProfileFixture.CandidateRotation(round, candidateCount));
                    trials.Add(RunProfiledTrial(
                        scorer, process, "steady_hot", round, set));
                }
            }

            foreach (int candidateCount in SentenceProfileFixture.CandidateCounts)
            {
                ProfileCandidateSet set = SentenceProfileFixture.Candidates(
                    candidateCount,
                    SentenceProfileFixture.CandidateRotation(parsed.Iterations, candidateCount));
                equivalenceChecks.Add(RunEquivalenceCheck(scorer, set));
            }

            foreach (CorrectionBenchmarkCase benchmarkCase in CorrectionBenchmarkCorpus.All)
                quality.Add(RunQualityCase(scorer, benchmarkCase));

            ProcessSnapshot afterHotBlocks = Snapshot(process);
            return new SentenceProfileReport(
                model.Label,
                model.Directory,
                parsed.Provider,
                SentenceProfileFixture.Seed,
                parsed.Iterations,
                Stopwatch.Frequency,
                loadTicks,
                beforeLoad,
                afterLoad,
                afterFirstScore,
                afterHotBlocks,
                warmups,
                overheadPairs,
                equivalenceChecks,
                trials,
                quality);
        }
        finally
        {
            scorer.Dispose();
        }
    }

    private static SentenceProfileTrial RunProfiledTrial(
        OnnxSentenceScorer scorer,
        Process process,
        string phase,
        int round,
        ProfileCandidateSet set)
    {
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        TimeSpan cpuBefore = process.TotalProcessorTime;
        TimedOutcome timed = RunProfiled(scorer, set.Texts);
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        long processCpuTicks = (process.TotalProcessorTime - cpuBefore).Ticks;
        process.Refresh();
        return new SentenceProfileTrial(
            phase,
            round,
            set.Texts.Count,
            set.CanonicalIndices,
            timed.ElapsedTicks,
            allocatedBytes,
            processCpuTicks,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            Summarize(timed.Outcome),
            timed.Profile!);
    }

    private static SentenceProfileWarmup RunWarmup(
        OnnxSentenceScorer scorer,
        ProfileCandidateSet set)
    {
        TimedOutcome timed = RunOrdinary(scorer, set.Texts);
        return new SentenceProfileWarmup(
            set.Texts.Count,
            timed.ElapsedTicks,
            Summarize(timed.Outcome));
    }

    private static SentenceProfileQualityCase RunQualityCase(
        OnnxSentenceScorer scorer,
        CorrectionBenchmarkCase benchmarkCase)
    {
        TimedOutcome profiled = RunProfiled(scorer, benchmarkCase.Candidates);
        TimedOutcome ordinary = RunOrdinary(scorer, benchmarkCase.Candidates);
        int chosen = profiled.Outcome.Chosen is null
            ? -1
            : Array.IndexOf(benchmarkCase.Candidates, profiled.Outcome.Chosen);
        OnnxSentenceOrderProfile[] orders = profiled.Profile!.Orders.ToArray();
        return new SentenceProfileQualityCase(
            benchmarkCase.Id,
            benchmarkCase.Category,
            benchmarkCase.Candidates.Length,
            benchmarkCase.LiteralIndex,
            benchmarkCase.GoldIndex,
            chosen,
            BestOriginalIndex(orders.Single(static order => order.Order == "forward")),
            BestOriginalIndex(orders.Single(static order => order.Order == "reverse")),
            profiled.Outcome.Margin,
            profiled.Outcome.AbstainReason,
            profiled.ElapsedTicks,
            OutcomesAreExactlyEquivalent(profiled.Outcome, ordinary.Outcome),
            profiled.Profile);
    }

    private static SentenceProfileEquivalenceCheck RunEquivalenceCheck(
        OnnxSentenceScorer scorer,
        ProfileCandidateSet set)
    {
        TimedOutcome profiled = RunProfiled(scorer, set.Texts);
        TimedOutcome ordinary = RunOrdinary(scorer, set.Texts);
        return new SentenceProfileEquivalenceCheck(
            set.Texts.Count,
            set.CanonicalIndices,
            Summarize(profiled.Outcome),
            Summarize(ordinary.Outcome),
            OutcomesAreExactlyEquivalent(profiled.Outcome, ordinary.Outcome));
    }

    private static int BestOriginalIndex(OnnxSentenceOrderProfile order)
    {
        OnnxSentenceCandidateProfile? best = order.Candidates
            .Where(static candidate => candidate.AbstainReason is null)
            .MaxBy(static candidate => candidate.Score);
        return best?.OriginalIndex ?? -1;
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

    internal static bool OutcomesAreExactlyEquivalent(
        SentenceScoringOutcome left,
        SentenceScoringOutcome right) =>
        string.Equals(left.Chosen, right.Chosen, StringComparison.Ordinal)
        && string.Equals(left.AbstainReason, right.AbstainReason, StringComparison.Ordinal)
        && left.Margin.Equals(right.Margin)
        && left.Threshold.Equals(right.Threshold)
        && left.Scores.SequenceEqual(right.Scores);

    private static bool IsError(SentenceProfileOutcome outcome) =>
        string.Equals(outcome.AbstainReason, "error", StringComparison.Ordinal);

    private static SentenceProfileOutcome Summarize(SentenceScoringOutcome outcome) => new(
        outcome.Chosen is null
            ? -1
            : outcome.Scores.ToList().FindIndex(score =>
                string.Equals(score.Text, outcome.Chosen, StringComparison.Ordinal)),
        outcome.Margin,
        outcome.Threshold,
        outcome.AbstainReason,
        outcome.Scores.Select(static score => score.Score).ToArray());

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

internal sealed record SentenceProfileReport(
    string ModelLabel,
    string ModelDirectory,
    string Provider,
    int Seed,
    int HotRounds,
    long StopwatchFrequency,
    long ModelLoadTicks,
    ProcessSnapshot BeforeLoad,
    ProcessSnapshot AfterLoad,
    ProcessSnapshot AfterFirstScore,
    ProcessSnapshot AfterHotBlocks,
    IReadOnlyList<SentenceProfileWarmup> Warmups,
    IReadOnlyList<SentenceProfileOverheadPair> OverheadPairs,
    IReadOnlyList<SentenceProfileEquivalenceCheck> EquivalenceChecks,
    IReadOnlyList<SentenceProfileTrial> Trials,
    IReadOnlyList<SentenceProfileQualityCase> QualityCases);

internal sealed record ProcessSnapshot(
    long TotalProcessorTicks,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long PeakWorkingSetBytes,
    long ManagedAllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

internal sealed record SentenceProfileWarmup(
    int CandidateCount,
    long ElapsedTicks,
    SentenceProfileOutcome Outcome);

internal sealed record SentenceProfileOverheadPair(
    int Pair,
    bool ProfiledFirst,
    long ProfiledTicks,
    long OrdinaryTicks,
    string? ProfiledAbstainReason,
    string? OrdinaryAbstainReason,
    bool EquivalentOutcome);

internal sealed record SentenceProfileEquivalenceCheck(
    int CandidateCount,
    IReadOnlyList<int> CandidatePermutation,
    SentenceProfileOutcome ProfiledOutcome,
    SentenceProfileOutcome OrdinaryOutcome,
    bool EquivalentOutcome);

internal sealed record SentenceProfileTrial(
    string Phase,
    int Round,
    int CandidateCount,
    IReadOnlyList<int> CandidatePermutation,
    long ElapsedTicks,
    long ManagedAllocatedBytes,
    long ProcessCpuTicks,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    SentenceProfileOutcome Outcome,
    OnnxSentenceScoringProfile Profile);

internal sealed record SentenceProfileOutcome(
    int ChosenPresentedIndex,
    double Margin,
    double Threshold,
    string? AbstainReason,
    IReadOnlyList<double> Scores);

internal sealed record SentenceProfileQualityCase(
    string Id,
    string Category,
    int CandidateCount,
    int LiteralIndex,
    int GoldIndex,
    int CombinedChosenIndex,
    int ForwardChosenIndex,
    int ReverseChosenIndex,
    double Margin,
    string? AbstainReason,
    long ElapsedTicks,
    bool EquivalentOutcome,
    OnnxSentenceScoringProfile Profile);
