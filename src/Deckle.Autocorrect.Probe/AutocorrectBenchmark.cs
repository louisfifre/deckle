using System.Diagnostics;

namespace Deckle.Autocorrect.Probe;

internal static class AutocorrectBenchmark
{
    public static AutocorrectBenchmarkReport Run(string dataDirectory, int iterations)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations));

        BenchmarkKnowledge knowledge = BenchmarkKnowledge.Load(dataDirectory);
        KeyboardQualitySummary quality =
            MeasureKeyboardQuality(knowledge, AutocorrectBenchmarkCorpus.All);

        AutocorrectPolicySet policies = CreatePolicySet(knowledge);
        WarmUp(knowledge, policies);

        var costs = new List<CommitCostSample>();
        using (var session = CreateProductionShapedSession(
            knowledge,
            policies,
            costSink: costs.Add))
        {
            for (int iteration = 0; iteration < iterations; iteration++)
                RunCorpus(session);
        }

        var candidateCollector = new CandidateCommitCollector();
        AutocorrectPolicySet observedPolicies =
            AutocorrectPolicySet.CreateWithCandidateSearchObserver(
                knowledge.French,
                knowledge.English,
                knowledge.AccentIndex,
                knowledge.Context,
                personal: null,
                personalVariants: null,
                verbs: knowledge.Verbs,
                candidateSearchObserver: candidateCollector.Observe);
        using (var session = CreateProductionShapedSession(
            knowledge,
            observedPolicies,
            candidateCollector: candidateCollector))
        {
            RunCorpus(session);
        }

        return AutocorrectBenchmarkReport.Create(
            iterations,
            quality,
            costs,
            candidateCollector.Samples);
    }

    public static KeyboardQualitySummary MeasureKeyboardQuality(string dataDirectory) =>
        MeasureKeyboardQuality(BenchmarkKnowledge.Load(dataDirectory), AutocorrectBenchmarkCorpus.All);

    // Same replay, caller-chosen corpus — the domain-pack bench types its own
    // gold sentences over an effective lexicon the product corpus never sees.
    public static KeyboardQualitySummary MeasureKeyboardQuality(
        string dataDirectory, IReadOnlyList<KeyboardScenario> scenarios) =>
        MeasureKeyboardQuality(BenchmarkKnowledge.Load(dataDirectory), scenarios);

    private static KeyboardQualitySummary MeasureKeyboardQuality(
        BenchmarkKnowledge knowledge, IReadOnlyList<KeyboardScenario> scenarios)
    {
        AutocorrectPolicySet policies = CreatePolicySet(knowledge);
        int trueChanges = 0;
        int wrongChanges = 0;
        int goldChanges = 0;
        int exactScenarios = 0;
        var failures = new List<string>();

        using var session = new BenchmarkKeyboardSession(
            policies.Policy,
            knowledge.French,
            knowledge.English,
            recordCorrections: true);

        foreach (KeyboardScenario scenario in scenarios)
        {
            session.BeginScenario();
            session.Type(scenario.Typed);

            goldChanges += scenario.Corrections.Count;
            var unmatched = scenario.Corrections.ToList();
            foreach (CorrectionDecision applied in session.Applied)
            {
                int match = unmatched.FindIndex(expected =>
                    expected.Original == applied.Original
                    && expected.Replacement == applied.Replacement);
                if (match >= 0)
                {
                    trueChanges++;
                    unmatched.RemoveAt(match);
                }
                else
                {
                    wrongChanges++;
                }
            }

            if (session.VisibleText == scenario.Expected)
                exactScenarios++;
            else
                failures.Add(
                    $"{scenario.Name}: '{session.VisibleText}' != '{scenario.Expected}'");
        }

        return new KeyboardQualitySummary(
            scenarios.Count,
            goldChanges,
            trueChanges,
            wrongChanges,
            exactScenarios,
            failures);
    }

    private static AutocorrectPolicySet CreatePolicySet(BenchmarkKnowledge knowledge) =>
        AutocorrectPolicySet.Create(
            knowledge.French,
            knowledge.English,
            knowledge.AccentIndex,
            knowledge.Context,
            verbs: knowledge.Verbs);

    private static void WarmUp(
        BenchmarkKnowledge knowledge,
        AutocorrectPolicySet policies)
    {
        using var session = CreateProductionShapedSession(knowledge, policies);
        RunCorpus(session);
    }

    private static BenchmarkKeyboardSession CreateProductionShapedSession(
        BenchmarkKnowledge knowledge,
        AutocorrectPolicySet policies,
        Action<CommitCostSample>? costSink = null,
        CandidateCommitCollector? candidateCollector = null) =>
        new(
            policies.Policy,
            knowledge.French,
            knowledge.English,
            probe: policies.AmbiguityProbe,
            reranker: new FrenchSentenceReranker(),
            costSink: costSink,
            candidateCollector: candidateCollector);

    private static void RunCorpus(BenchmarkKeyboardSession session)
    {
        foreach (KeyboardScenario scenario in AutocorrectBenchmarkCorpus.All)
        {
            session.BeginScenario();
            session.Type(scenario.Typed);
        }
    }
}

internal sealed record BenchmarkKnowledge(
    FrequencyLexicon French,
    IFrequencyLexicon English,
    AccentIndex AccentIndex,
    IPairDisambiguator? Context,
    VerbMorphology? Verbs)
{
    public static BenchmarkKnowledge Load(string dataDirectory)
    {
        string frenchPath = Path.Combine(
            dataDirectory, AutocorrectLexiconArtifacts.FrenchFileName);
        if (!File.Exists(frenchPath))
            throw new FileNotFoundException(
                "The packaged French autocorrect lexicon was not found.", frenchPath);

        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(frenchPath);
        var english = new GlobalEnglishLexicon(
            AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDirectory));

        string contextPath = Path.Combine(
            dataDirectory, AutocorrectLexiconArtifacts.PairBigramsFrenchFileName);
        IPairDisambiguator? context = File.Exists(contextPath)
            ? BigramPairDisambiguator.LoadTsvGz(contextPath)
            : null;

        string verbsPath = Path.Combine(
            dataDirectory, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName);
        VerbMorphology? verbs = File.Exists(verbsPath)
            ? VerbMorphology.LoadTsvGz(verbsPath)
            : null;

        return new BenchmarkKnowledge(
            french,
            english,
            AccentIndex.Build(french),
            context,
            verbs);
    }
}

internal sealed record KeyboardQualitySummary(
    int ScenarioCount,
    int GoldChanges,
    int TrueChanges,
    int WrongChanges,
    int ExactScenarios,
    IReadOnlyList<string> Failures)
{
    public double Precision => TrueChanges + WrongChanges == 0
        ? 1.0
        : (double)TrueChanges / (TrueChanges + WrongChanges);

    public double Recall => GoldChanges == 0
        ? 1.0
        : (double)TrueChanges / GoldChanges;

    public double ExactRate => ScenarioCount == 0
        ? 1.0
        : (double)ExactScenarios / ScenarioCount;
}

internal sealed record AutocorrectBenchmarkReport(
    int Iterations,
    KeyboardQualitySummary Quality,
    int CommitSampleCount,
    MetricDistribution LatencyMicroseconds,
    MetricDistribution AllocatedBytes,
    IReadOnlyList<CommitCostSample> SlowestCommits,
    IReadOnlyList<CommitCostSample> LargestAllocations,
    int CandidateSampleCount,
    MetricDistribution GeneratedCandidates,
    MetricDistribution DistinctCandidateLookups,
    MetricDistribution MatchedCandidates,
    long CommitCandidateGenerations,
    long SentenceCandidateGenerations,
    IReadOnlyList<CandidateCommitSample> LargestCandidateSearches)
{
    public long TotalCandidateGenerations =>
        CommitCandidateGenerations + SentenceCandidateGenerations;

    public static AutocorrectBenchmarkReport Create(
        int iterations,
        KeyboardQualitySummary quality,
        IReadOnlyList<CommitCostSample> costs,
        IReadOnlyList<CandidateCommitSample> candidates)
    {
        double tickToMicrosecond = 1_000_000.0 / Stopwatch.Frequency;
        return new AutocorrectBenchmarkReport(
            iterations,
            quality,
            costs.Count,
            MetricDistribution.Create(
                costs.Select(sample => sample.ElapsedTicks * tickToMicrosecond)),
            MetricDistribution.Create(
                costs.Select(sample => (double)sample.AllocatedBytes)),
            SlowestByWord(costs),
            LargestAllocationByWord(costs),
            candidates.Count,
            MetricDistribution.Create(
                candidates.Select(sample => (double)sample.Generated)),
            MetricDistribution.Create(
                candidates.Select(sample => (double)sample.DistinctLookups)),
            MetricDistribution.Create(
                candidates.Select(sample => (double)sample.Matches)),
            candidates.Sum(sample => (long)sample.CommitGenerated),
            candidates.Sum(sample => (long)sample.SentenceGenerated),
            candidates
                .OrderByDescending(sample => sample.Generated)
                .ThenBy(sample => sample.Word, StringComparer.Ordinal)
                .Take(5)
                .ToArray());
    }

    private static IReadOnlyList<CommitCostSample> SlowestByWord(
        IReadOnlyList<CommitCostSample> costs) =>
        costs
            .GroupBy(sample => sample.Word, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(sample => sample.ElapsedTicks)
                .First())
            .OrderByDescending(sample => sample.ElapsedTicks)
            .Take(5)
            .ToArray();

    private static IReadOnlyList<CommitCostSample> LargestAllocationByWord(
        IReadOnlyList<CommitCostSample> costs) =>
        costs
            .GroupBy(sample => sample.Word, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(sample => sample.AllocatedBytes)
                .First())
            .OrderByDescending(sample => sample.AllocatedBytes)
            .Take(5)
            .ToArray();
}

internal readonly record struct MetricDistribution(
    double P50,
    double P95,
    double Maximum)
{
    // Nearest-rank percentiles: the smallest observed value whose cumulative
    // sample share reaches the requested percentile. No interpolation invents
    // a latency or allocation count that never occurred.
    public static MetricDistribution Create(IEnumerable<double> source)
    {
        double[] values = source.OrderBy(static value => value).ToArray();
        if (values.Length == 0)
            return default;
        return new MetricDistribution(
            Percentile(values, 0.50),
            Percentile(values, 0.95),
            values[^1]);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        int index = Math.Clamp(
            (int)Math.Ceiling(percentile * sorted.Count) - 1,
            0,
            sorted.Count - 1);
        return sorted[index];
    }
}
