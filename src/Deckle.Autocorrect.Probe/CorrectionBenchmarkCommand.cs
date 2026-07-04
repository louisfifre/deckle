using System.Diagnostics;
using System.Reflection;
using Deckle.Autocorrect.Onnx;

namespace Deckle.Autocorrect.Probe;

internal static class CorrectionBenchmarkCommand
{
    private const double PrimaryThreshold = 0.25;

    public static int Run(ProbeArguments parsed)
    {
        if (parsed.Models.Count == 0)
        {
            Console.Error.WriteLine("No staged benchmark model found.");
            return 1;
        }

        if (parsed.Models.Count > 1)
            return RunBatch(parsed);

        Console.WriteLine($"Cases     : {CorrectionBenchmarkCorpus.All.Count}");
        Console.WriteLine($"Provider  : {parsed.Provider}");
        Console.WriteLine($"Thresholds: {string.Join(", ", parsed.Thresholds.Select(static t => t.ToString("0.##")))}");
        Console.WriteLine();

        int loaded = 0;
        foreach (ModelSpec model in parsed.Models)
        {
            if (!Directory.Exists(model.Directory))
            {
                Console.WriteLine($"Model: {model.Label}");
                Console.WriteLine($"  missing: {model.Directory}");
                Console.WriteLine();
                continue;
            }

            ISentenceScorer? scorer = OnnxSentenceScorer.TryLoad(model.Directory, margin: 0.0, parsed.Provider);
            if (scorer is null)
            {
                Console.WriteLine($"Model: {model.Label}");
                Console.WriteLine($"  failed to load as an ONNX Runtime GenAI model on provider '{parsed.Provider}'.");
                Console.WriteLine();
                continue;
            }

            loaded++;
            try
            {
                IReadOnlyList<CorrectionBenchmarkResult> results = RunModel(model, scorer);
                PrintModelSummary(model, results, parsed);
            }
            finally
            {
                (scorer as IDisposable)?.Dispose();
            }
        }

        return loaded > 0 ? 0 : 1;
    }

    private static int RunBatch(ProbeArguments parsed)
    {
        string? assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            Console.Error.WriteLine("Cannot resolve probe assembly path for model-isolated benchmark run.");
            return 1;
        }

        int exitCode = 0;
        foreach (ModelSpec model in parsed.Models)
        {
            using var process = new Process();
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.ArgumentList.Add(assemblyPath);
            process.StartInfo.ArgumentList.Add("--benchmark");
            process.StartInfo.ArgumentList.Add("--model");
            process.StartInfo.ArgumentList.Add($"{model.Label}={model.Directory}");
            process.StartInfo.ArgumentList.Add("--provider");
            process.StartInfo.ArgumentList.Add(parsed.Provider);
            foreach (double threshold in parsed.Thresholds)
            {
                process.StartInfo.ArgumentList.Add("--threshold");
                process.StartInfo.ArgumentList.Add(threshold.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (parsed.ShowCases)
                process.StartInfo.ArgumentList.Add("--show-cases");

            // Forward the child's stderr live (its per-case progress) and read stdout
            // (the summary, emitted at the end) on this thread. Reading one stream fully
            // before the other deadlocks once the child fills the unread pipe's buffer —
            // the likely cause of a batch run appearing to hang with no visible output.
            process.ErrorDataReceived += static (_, e) =>
            {
                if (e.Data is not null)
                    Console.Error.WriteLine(e.Data);
            };
            process.Start();
            process.BeginErrorReadLine();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Console.Write(output);

            if (process.ExitCode != 0)
                exitCode = process.ExitCode;
        }

        return exitCode;
    }

    private static IReadOnlyList<CorrectionBenchmarkResult> RunModel(ModelSpec model, ISentenceScorer scorer)
    {
        int total = CorrectionBenchmarkCorpus.All.Count;
        var results = new List<CorrectionBenchmarkResult>(total);
        int index = 0;
        foreach (CorrectionBenchmarkCase benchmarkCase in CorrectionBenchmarkCorpus.All)
        {
            var stopwatch = Stopwatch.StartNew();
            SentenceScoringOutcome outcome = scorer.Score(benchmarkCase.Candidates);
            stopwatch.Stop();

            results.Add(CorrectionBenchmarkResult.FromOutcome(
                benchmarkCase,
                outcome,
                stopwatch.Elapsed));

            // One line per case to stderr so a slow model (seconds per case on the CPU
            // EP) shows life rather than a silent multi-minute wait; stdout stays the
            // clean summary. The load-bearing timing lives in each result, not here.
            index++;
            Console.Error.WriteLine(
                $"  [{model.Label}] {index}/{total} {benchmarkCase.Id} {stopwatch.Elapsed.TotalMilliseconds:0}ms");
        }

        return results;
    }

    private static void PrintModelSummary(
        ModelSpec model,
        IReadOnlyList<CorrectionBenchmarkResult> results,
        ProbeArguments parsed)
    {
        Console.WriteLine($"Model: {model.Label}");
        Console.WriteLine($"Path : {model.Directory}");
        Console.WriteLine($"EP   : {parsed.Provider}");
        Console.WriteLine($"Time : {results.Sum(static r => r.Duration.TotalSeconds):0.0}s");
        Console.WriteLine();
        Console.WriteLine("threshold  changes  fixes  wrong  precision  recall  safe_abs  miss_abs  miss_keep  errors");

        foreach (double threshold in parsed.Thresholds)
        {
            CorrectionBenchmarkSummary summary = CorrectionBenchmarkSummary.Create(results, threshold);
            Console.WriteLine(
                $"{summary.Threshold,9:0.##}  {summary.Changes,7}  {summary.Fixes,5}  {summary.WrongChanges,5}  {FormatPercent(summary.ChangePrecision),9}  {FormatPercent(summary.CorrectionRecall),6}  {summary.SafeAbstentions,8}  {summary.AbstainedCorrections,8}  {summary.MissedKeeps,9}  {summary.ScoringErrors,6}");
        }

        Console.WriteLine();
        PrintCategorySummary(results);
        Console.WriteLine();
        PrintCaseFindings(results, parsed.ShowCases);
        Console.WriteLine();
    }

    private static void PrintCategorySummary(IReadOnlyList<CorrectionBenchmarkResult> results)
    {
        Console.WriteLine($"Categories at threshold {PrimaryThreshold:0.##}:");
        Console.WriteLine("category       cases  correctable  fixes  wrong  misses  errors");

        foreach (IGrouping<string, CorrectionBenchmarkResult> group in results.GroupBy(static r => r.Case.Category).OrderBy(static g => g.Key))
        {
            CorrectionBenchmarkSummary summary = CorrectionBenchmarkSummary.Create(group.ToArray(), PrimaryThreshold);
            Console.WriteLine(
                $"{group.Key,-13}  {summary.Total,5}  {summary.Correctable,11}  {summary.Fixes,5}  {summary.WrongChanges,5}  {summary.Misses,6}  {summary.ScoringErrors,6}");
        }
    }

    private static void PrintCaseFindings(
        IReadOnlyList<CorrectionBenchmarkResult> results,
        bool showCases)
    {
        IEnumerable<CorrectionBenchmarkResult> selected = showCases
            ? results
            : results.Where(static result =>
            {
                CorrectionBenchmarkVerdict verdict = result.Verdict(PrimaryThreshold);
                return verdict is CorrectionBenchmarkVerdict.WrongChange
                    or CorrectionBenchmarkVerdict.MissedKeep
                    or CorrectionBenchmarkVerdict.AbstainedCorrection
                    or CorrectionBenchmarkVerdict.ScoringError;
            });

        Console.WriteLine(showCases
            ? $"Cases at threshold {PrimaryThreshold:0.##}:"
            : $"Findings at threshold {PrimaryThreshold:0.##}:");

        foreach (CorrectionBenchmarkResult result in selected)
        {
            CorrectionBenchmarkVerdict verdict = result.Verdict(PrimaryThreshold);
            string best = result.BestText ?? "(none)";
            Console.WriteLine(
                $"{verdict,-20}  margin={result.Margin,6:0.000}  time={result.Duration.TotalMilliseconds,7:0}ms  {result.Case.Id} [{result.Case.Category}]");
            Console.WriteLine($"  literal: {result.Case.Literal}");
            Console.WriteLine($"  gold   : {result.Case.Gold}");
            Console.WriteLine($"  best   : {best}");
            if (result.AbstainReason is not null)
                Console.WriteLine($"  reason : {result.AbstainReason}");
        }
    }

    private static string FormatPercent(double value) =>
        double.IsNaN(value)
            ? "N/A"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{value * 100.0:0}%");
}
