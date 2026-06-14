namespace Deckle.Benchmark.PhiBench;

/// <summary>
/// PhiBench — Phi-4-Multimodal ONNX/DirectML POC bench for Deckle.
///
/// Two subcommands :
///
///   single  : transcribe ONE audio, write the result to stdout as JSON.
///             Used for sanity checks (palier 1).
///
///   corpus  : iterate over a Deckle telemetry corpus, write one JSONL row per
///             (sample, regime). Row schema mirrors benchmark/benches/voxtral-validation/bench.py
///             so the existing Python post-processors (metrics, Gemini judge) can
///             consume the output unchanged.
///
/// Usage :
///   PhiBench single --model-path D:\models\llm\phi4-multimodal-onnx\gpu\gpu-int4-rtn-block-32
///                   --audio path\to\file.wav [--prompt "..."] [--max-new-tokens N]
///
///   PhiBench corpus --model-path D:\models\llm\phi4-multimodal-onnx\gpu\gpu-int4-rtn-block-32
///                   --corpus %LOCALAPPDATA%\Deckle\benchmark\corpora\voxtral-val-30
///                   --regimes ..\..\prompts\transcription\voxtral_validation.toml
///                   --output run-results.jsonl
///                   [--only T1_baseline] [--limit N] [--max-new-tokens N]
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var subcommand = args[0];
        var rest = args[1..];
        var opts = ParseOptions(rest);

        try
        {
            return subcommand switch
            {
                "single" => await SingleRunner.RunAsync(opts),
                "corpus" => await CorpusRunner.RunAsync(opts),
                "--help" or "-h" => PrintUsageAndReturn(0),
                _ => PrintUsageAndReturn(1, $"Unknown subcommand: {subcommand}"),
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            return 2;
        }
    }

    /// <summary>Parses --key value pairs into a dictionary. Bool flags use --flag (no value).</summary>
    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--"))
                throw new ArgumentException($"Expected --option, got '{arg}'.");
            var key = arg[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            {
                dict[key] = args[++i];
            }
            else
            {
                dict[key] = "true";
            }
        }
        return dict;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("PhiBench — Phi-4-Multimodal ONNX/DirectML bench for Deckle\n");
        Console.Error.WriteLine("Subcommands:");
        Console.Error.WriteLine("  single   Transcribe one audio file, write JSON to stdout.");
        Console.Error.WriteLine("  corpus   Bench a corpus, write one row per (sample, regime) to a JSONL file.\n");
        Console.Error.WriteLine("Run with --help for the full option list of each subcommand.");
    }

    private static int PrintUsageAndReturn(int code, string? error = null)
    {
        if (error != null) Console.Error.WriteLine($"FATAL: {error}\n");
        PrintUsage();
        return code;
    }
}
