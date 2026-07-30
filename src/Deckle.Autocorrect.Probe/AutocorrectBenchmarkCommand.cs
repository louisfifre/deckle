using System.Globalization;
using System.Text.Json;

namespace Deckle.Autocorrect.Probe;

internal static class AutocorrectBenchmarkCommand
{
    public static int Run(ProbeArguments parsed)
    {
        string? dataDirectory = ResolveDataDirectory();
        if (dataDirectory is null)
        {
            Console.Error.WriteLine(
                "Packaged autocorrect data was not found beside the probe output.");
            return 1;
        }

        AutocorrectBenchmarkReport report;
        try
        {
            report = AutocorrectBenchmark.Run(dataDirectory, parsed.Iterations);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }

        if (parsed.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));
        }
        else
        {
            Print(report);
        }
        return report.Quality.WrongChanges == 0 ? 0 : 3;
    }

    private static void Print(AutocorrectBenchmarkReport report)
    {
        KeyboardQualitySummary quality = report.Quality;
        Console.WriteLine("Autocorrect deterministic baseline");
        Console.WriteLine($"Corpus     : {quality.ScenarioCount} keyboard scenarios");
        Console.WriteLine($"Iterations : {report.Iterations}");
        Console.WriteLine();
        Console.WriteLine("Keyboard quality (deterministic commit stage)");
        Console.WriteLine(
            $"  internal edit-pair precision     : {Percent(quality.InternalEditPairPrecision),6}  "
            + $"({quality.TrueChanges}/{quality.TrueChanges + quality.WrongChanges})");
        Console.WriteLine(
            "  applied correction precision     :    N/A  "
            + "(no observed target postcondition)");
        Console.WriteLine(
            $"  recall    : {Percent(quality.Recall),6}  "
            + $"({quality.TrueChanges}/{quality.GoldChanges})");
        Console.WriteLine(
            $"  exact     : {Percent(quality.ExactRate),6}  "
            + $"({quality.ExactScenarios}/{quality.ScenarioCount})");
        Console.WriteLine($"  wrong     : {quality.WrongChanges}");
        foreach (string failure in quality.Failures)
            Console.WriteLine($"  residue   : {failure}");

        Console.WriteLine();
        Console.WriteLine(
            $"Managed commit hot path ({report.CommitSampleCount} word boundaries; "
            + "sentence-slot preparation included)");
        PrintDistribution("latency_us", report.LatencyMicroseconds, "0.0");
        PrintDistribution("allocated_b", report.AllocatedBytes, "0");
        Console.WriteLine("  slowest:");
        foreach (CommitCostSample sample in report.SlowestCommits)
        {
            Console.WriteLine(
                $"    {sample.Word,-18} latency_us={Format(sample.LatencyMicroseconds, "0.0"),10} "
                + $"allocated_b={sample.AllocatedBytes,10}");
        }
        Console.WriteLine("  largest allocations:");
        foreach (CommitCostSample sample in report.LargestAllocations)
        {
            Console.WriteLine(
                $"    {sample.Word,-18} allocated_b={sample.AllocatedBytes,10} "
                + $"latency_us={Format(sample.LatencyMicroseconds, "0.0"),10}");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Candidate search ({report.CandidateSampleCount} word boundaries; one corpus pass)");
        PrintDistribution("generated", report.GeneratedCandidates, "0");
        PrintDistribution("lookups", report.DistinctCandidateLookups, "0");
        PrintDistribution("matches", report.MatchedCandidates, "0");
        Console.WriteLine(
            $"  generated_total={report.TotalCandidateGenerations} "
            + $"commit={report.CommitCandidateGenerations} "
            + $"sentence={report.SentenceCandidateGenerations}");
        Console.WriteLine("  largest:");
        foreach (CandidateCommitSample sample in report.LargestCandidateSearches)
        {
            Console.WriteLine(
                $"    {sample.Word,-18} generated={sample.Generated,8} "
                + $"commit={sample.CommitGenerated,8} sentence={sample.SentenceGenerated,8} "
                + $"lookups={sample.DistinctLookups,8} matches={sample.Matches,4}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "Definitions: nearest-rank p50/p95; allocations are managed bytes on the "
            + "current thread; latency spans the synchronous managed boundary handler "
            + "with simulated OS ports and excludes background model inference.");
    }

    private static void PrintDistribution(
        string name,
        MetricDistribution distribution,
        string format)
    {
        Console.WriteLine(
            $"  {name,-11} n={distribution.Count,8} "
            + $"p50={FormatMetric(distribution.P50, format),10} "
            + $"p95={FormatMetric(distribution.P95, format),10} "
            + $"p99={FormatMetric(distribution.P99, format),10} "
            + $"max={FormatMetric(distribution.Maximum, format),10}");
    }

    private static string FormatMetric(double? value, string format) =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? "N/A";

    private static string Percent(double? value) =>
        value?.ToString("P1", CultureInfo.InvariantCulture) ?? "N/A";

    private static string Format(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);

    internal static string? ResolveDataDirectory()
    {
        string direct = Path.Combine(AppContext.BaseDirectory, "Data");
        if (HasFrenchLexicon(direct))
            return direct;

        string transitive = Path.Combine(
            AppContext.BaseDirectory, "Deckle.Autocorrect", "Data");
        return HasFrenchLexicon(transitive) ? transitive : null;
    }

    private static bool HasFrenchLexicon(string directory) =>
        File.Exists(Path.Combine(
            directory, AutocorrectLexiconArtifacts.FrenchFileName));
}
