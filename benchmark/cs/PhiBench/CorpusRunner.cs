using System.Diagnostics;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Benchmark.PhiBench;

/// <summary>
/// Handles <c>PhiBench corpus ...</c> — iterates a corpus × regimes,
/// writes one JSONL row per (sample, regime) to the output file. Mirrors
/// the loop structure of benchmark/benches/voxtral-validation/bench.py
/// (palier 2 of the POC).
///
/// Metrics and judge are NOT computed here — Python downstream consumes the
/// JSONL to add WER/CER/looping (lib.metrics) and Gemini scoring
/// (lib.judges.gemini). This keeps the C# layer focused on the inference
/// path that production will use.
/// </summary>
public static class CorpusRunner
{
    private const string SourceName = "phi4-onnx-dml";
    private const string SourceLabel = "Phi-4-multimodal-instruct ONNX INT4 via DirectML";

    public static async Task<int> RunAsync(Dictionary<string, string> opts)
    {
        var modelPath = Require(opts, "model-path");
        var corpusDir = Require(opts, "corpus");
        var regimesPath = Require(opts, "regimes");
        var outputPath = Require(opts, "output");
        var only = opts.GetValueOrDefault("only", "all");
        var limit = int.Parse(opts.GetValueOrDefault("limit", "0"));
        var maxTokens = int.Parse(opts.GetValueOrDefault("max-new-tokens", "1024"));
        var executionProvider = opts.GetValueOrDefault("execution-provider", "dml");

        var samples = CorpusLoader.Load(corpusDir);
        if (limit > 0 && samples.Count > limit) samples = samples.GetRange(0, limit);
        var regimes = RegimesLoader.Load(regimesPath, only);

        if (samples.Count == 0) { Console.Error.WriteLine("FATAL: no samples in corpus"); return 2; }
        if (regimes.Count == 0) { Console.Error.WriteLine("FATAL: no regimes selected"); return 2; }

        Console.Error.WriteLine($"=== PhiBench corpus ===");
        Console.Error.WriteLine($"  corpus     : {corpusDir} ({samples.Count} samples)");
        Console.Error.WriteLine($"  regimes    : {string.Join(",", regimes.ConvertAll(r => r.Name))}");
        Console.Error.WriteLine($"  output     : {outputPath}");
        Console.Error.WriteLine($"  model_path : {modelPath}");
        Console.Error.WriteLine($"  ep         : {executionProvider}");
        Console.Error.WriteLine($"  max_new    : {maxTokens}");

        using var ogaHandle = new OgaHandle();
        Console.Error.WriteLine($"\n  loading model...");
        var loadStopwatch = Stopwatch.StartNew();
        using var transcriber = new Phi4Transcriber(modelPath, executionProvider);
        loadStopwatch.Stop();
        Console.Error.WriteLine($"  model ready ({loadStopwatch.Elapsed.TotalSeconds:F1}s)");

        // Warmup on the shortest sample to absorb first-call JIT cost outside
        // the measurement loop.
        Console.Error.WriteLine($"  warmup on {Path.GetFileName(samples[0].AudioPath)}...");
        transcriber.Warmup(samples[0].AudioPath);

        using var writer = new JsonlWriter(outputPath);
        var totalStopwatch = Stopwatch.StartNew();
        int total = 0, fail = 0;

        foreach (var (sample, si) in samples.Select((s, i) => (s, i + 1)))
        {
            foreach (var regime in regimes)
            {
                var tag = $"[{si}/{samples.Count} {regime.Name}]";
                Console.Error.Write($"{tag} {sample.Id[..8]}… ({sample.DurationSeconds,5:F1}s) ");

                var result = transcriber.Transcribe(
                    audioPath: sample.AudioPath,
                    userPrompt: regime.Prompt,
                    systemPrompt: string.IsNullOrEmpty(regime.SystemPrompt) ? null : regime.SystemPrompt,
                    maxNewTokens: maxTokens,
                    audioSecondsOverride: sample.DurationSeconds);

                writer.WriteRow(sample, regime, SourceName, SourceLabel, result);
                total++;
                if (!result.Ok)
                {
                    fail++;
                    Console.Error.WriteLine($"FAIL {result.Error[..Math.Min(80, result.Error.Length)]}");
                }
                else
                {
                    Console.Error.WriteLine($"RTF {result.Rtf:F2}  tokens {result.GeneratedTokens}  [{result.ElapsedSeconds:F1}s]");
                }
            }
        }
        totalStopwatch.Stop();
        Console.Error.WriteLine($"\n✓ done — {total} rows ({fail} fail) in {totalStopwatch.Elapsed.TotalSeconds:F1}s");
        Console.Error.WriteLine($"  results : {outputPath}");
        await Task.CompletedTask;
        return 0;
    }

    private static string Require(Dictionary<string, string> opts, string key)
    {
        if (!opts.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
            throw new ArgumentException($"Missing required option --{key}");
        return value;
    }
}
