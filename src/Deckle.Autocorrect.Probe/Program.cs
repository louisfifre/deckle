using Deckle.Autocorrect;
using Deckle.Autocorrect.Onnx;
using Deckle.Core;

ProbeArguments? parsed = ProbeArguments.Parse(args);
if (parsed is null)
{
    PrintUsage();
    return 2;
}

string modelDir = parsed.ModelDir ?? Path.Combine(
    AppPaths.ModelsDirectory,
    "qwen3-0.6b-onnx",
    "onnxruntime",
    "cpu_and_mobile",
    "cpu-int4-kld-block-128");
if (!Directory.Exists(modelDir))
{
    Console.Error.WriteLine($"Missing model directory: {modelDir}");
    return 1;
}

Console.WriteLine($"Model     : {modelDir}");
Console.WriteLine($"Margin    : {parsed.Margin:0.###}");
Console.WriteLine($"Candidates: {parsed.Candidates.Count}");
Console.WriteLine();

ISentenceScorer? scorer = OnnxSentenceScorer.TryLoad(modelDir, parsed.Margin);
if (scorer is null)
{
    Console.Error.WriteLine("Model failed to load as an ONNX Runtime GenAI model.");
    return 1;
}

try
{
    SentenceScoringOutcome outcome = scorer.Score(parsed.Candidates);
    if (outcome.Scores.Count > 0)
    {
        foreach (SentenceCandidateScore score in outcome.Scores.OrderByDescending(static s => s.Score))
        {
            Console.WriteLine(
                $"{score.Score,10:0.000}  logp={score.LogProbability,10:0.000}  tokens={score.ScoredTokenCount,3}  {score.Text}");
        }

        Console.WriteLine();
    }

    Console.WriteLine($"Chosen    : {outcome.Chosen ?? "(abstain)"}");
    Console.WriteLine($"Margin    : {outcome.Margin:0.###}");
    Console.WriteLine($"Threshold : {outcome.Threshold:0.###}");
    Console.WriteLine($"Abstain   : {outcome.AbstainReason ?? "(none)"}");

    return outcome.AbstainReason is null ? 0 : 3;
}
finally
{
    (scorer as IDisposable)?.Dispose();
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Deckle.Autocorrect.Probe --model <dir> [--margin <n>] --candidate <text> --candidate <text> [...]");
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        "If --model is omitted, the default is %LOCALAPPDATA%\\Deckle\\models\\qwen3-0.6b-onnx\\onnxruntime\\cpu_and_mobile\\cpu-int4-kld-block-128.");
}

internal sealed class ProbeArguments
{
    public string? ModelDir { get; private init; }
    public double Margin { get; private init; } = 0.0;
    public required IReadOnlyList<string> Candidates { get; init; }

    public static ProbeArguments? Parse(string[] args)
    {
        string? modelDir = Environment.GetEnvironmentVariable("DECKLE_ONNX_JUDGE_MODEL_DIR");
        double margin = 0.0;
        var candidates = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h" or "/?")
                return null;

            if (arg is "--model" or "-m")
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    return null;
                modelDir = args[i];
                continue;
            }

            if (arg is "--margin")
            {
                if (++i >= args.Length ||
                    !double.TryParse(args[i], System.Globalization.CultureInfo.InvariantCulture, out margin))
                    return null;
                continue;
            }

            if (arg is "--candidate" or "-c")
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    return null;
                candidates.Add(args[i]);
                continue;
            }

            return null;
        }

        if (candidates.Count < 2)
            return null;

        return new ProbeArguments
        {
            ModelDir = modelDir,
            Margin = margin,
            Candidates = candidates,
        };
    }
}
