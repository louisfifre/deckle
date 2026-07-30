using System.Globalization;

namespace Deckle.Autocorrect.Probe;

internal enum ProbeMode
{
    Single,
    Benchmark,
    AutocorrectBenchmark,
    StaleWork,
    AnticipationLead,
    AnticipationTransactionJoin,
    SentenceProfile,
    SentenceCalibration,
    SentenceCanonicalLatency,
    SentenceOrderAblation,
    SentenceBatchTokenization,
    SentenceBatchExperiment,
    SentenceDecisionInventory,
    SentenceUnanimityBundle,
    CaretContext,
}

internal sealed class ProbeArguments
{
    public ProbeMode Mode { get; private init; }
    public required IReadOnlyList<ModelSpec> Models { get; init; }
    public double Margin { get; private init; }
    public required IReadOnlyList<double> Thresholds { get; init; }
    public required IReadOnlyList<string> Candidates { get; init; }
    public bool ShowCases { get; private init; }
    public bool Json { get; private init; }
    public int Iterations { get; private init; }
    public int DelaySeconds { get; private init; }
    public int MaxCharacters { get; private init; }
    public string StreamPath { get; private init; } = string.Empty;
    public long StreamBytes { get; private init; }

    // The ONNX Runtime GenAI execution provider the judge loads onto: "dml" drives
    // the forced-decoding judge on the GPU (DirectML), "cpu" the built-in CPU EP.
    // The scorer selects it in code, so one export can be benchmarked on either —
    // the lever for the CPU-int4-vs-DirectML comparison. Defaults to "dml".
    public string Provider { get; private init; } = "dml";

    public static ProbeArguments? Parse(string[] args)
    {
        ProbeMode mode = ProbeMode.Single;
        double margin = 0.0;
        var models = new List<ModelSpec>();
        var thresholds = new List<double>();
        var candidates = new List<string>();
        bool showCases = false;
        bool json = false;
        string provider = "dml";
        int iterations = 20;
        int delaySeconds = 5;
        int maxCharacters = 1024;
        bool iterationsSpecified = false;
        bool delaySpecified = false;
        bool maxCharactersSpecified = false;
        string? streamPath = null;
        long streamBytes = 0;
        bool streamBytesSpecified = false;
        bool modeSelected = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h" or "/?")
                return null;

            if (arg is "--benchmark")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.Benchmark;
                modeSelected = true;
                continue;
            }

            if (arg is "--autocorrect-benchmark")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.AutocorrectBenchmark;
                modeSelected = true;
                continue;
            }

            if (arg is "--stale-work-probe")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.StaleWork;
                modeSelected = true;
                continue;
            }

            if (arg is "--anticipation-lead-oracle")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.AnticipationLead;
                modeSelected = true;
                continue;
            }

            if (arg is "--anticipation-transaction-join")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.AnticipationTransactionJoin;
                modeSelected = true;
                continue;
            }

            if (arg is "--sentence-profile")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.SentenceProfile;
                modeSelected = true;
                continue;
            }

            if (arg is "--sentence-calibration")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.SentenceCalibration;
                modeSelected = true;
                continue;
            }

            if (arg is "--sentence-canonical-latency")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.SentenceCanonicalLatency;
                modeSelected = true;
                continue;
            }

            if (arg is "--sentence-order-ablation")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.SentenceOrderAblation;
                modeSelected = true;
                continue;
            }

            if (arg is "--sentence-batch-experiment")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.SentenceBatchExperiment;
                modeSelected = true;
                continue;
            }

            if (arg is "--sentence-batch-tokenization")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.SentenceBatchTokenization;
                modeSelected = true;
                continue;
            }

            if (arg is "--sentence-decision-inventory")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.SentenceDecisionInventory;
                modeSelected = true;
                continue;
            }

            if (arg is "--sentence-unanimity-bundle")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.SentenceUnanimityBundle;
                modeSelected = true;
                continue;
            }

            if (arg is "--caret-context")
            {
                if (modeSelected)
                    return null;
                mode = ProbeMode.CaretContext;
                modeSelected = true;
                continue;
            }

            if (arg is "--iterations")
            {
                if (++i >= args.Length
                    || !int.TryParse(args[i], out iterations)
                    || iterations < 1)
                    return null;
                iterationsSpecified = true;
                continue;
            }

            if (arg is "--delay")
            {
                if (++i >= args.Length
                    || !int.TryParse(args[i], out delaySeconds)
                    || delaySeconds is < 1 or > 30)
                    return null;
                delaySpecified = true;
                continue;
            }

            if (arg is "--max-chars")
            {
                if (++i >= args.Length
                    || !int.TryParse(args[i], out maxCharacters)
                    || maxCharacters is < 64 or > 4096)
                    return null;
                maxCharactersSpecified = true;
                continue;
            }

            if (arg is "--stream")
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    return null;
                streamPath = args[i];
                continue;
            }

            if (arg is "--stream-bytes")
            {
                if (++i >= args.Length
                    || !long.TryParse(args[i], out streamBytes)
                    || streamBytes < 1)
                    return null;
                streamBytesSpecified = true;
                continue;
            }

            if (arg is "--show-cases")
            {
                showCases = true;
                continue;
            }

            if (arg is "--json")
            {
                json = true;
                continue;
            }

            if (arg is "--model" or "-m")
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    return null;

                models.Add(ModelSpec.Parse(args[i]));
                continue;
            }

            if (arg is "--margin")
            {
                if (++i >= args.Length ||
                    !double.TryParse(args[i], CultureInfo.InvariantCulture, out margin))
                    return null;

                continue;
            }

            if (arg is "--provider" or "-e")
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    return null;

                provider = args[i].Trim();
                continue;
            }

            if (arg is "--threshold" or "-t")
            {
                if (++i >= args.Length ||
                    !double.TryParse(args[i], CultureInfo.InvariantCulture, out double threshold))
                    return null;

                thresholds.Add(Math.Max(0.0, threshold));
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

        if (iterationsSpecified
            && mode is not ProbeMode.AutocorrectBenchmark
                and not ProbeMode.StaleWork
                and not ProbeMode.SentenceProfile)
            return null;
        if ((delaySpecified || maxCharactersSpecified) && mode != ProbeMode.CaretContext)
            return null;
        if (streamPath is not null
            && mode is not ProbeMode.AnticipationLead
                and not ProbeMode.AnticipationTransactionJoin)
            return null;
        if (streamBytesSpecified
            && mode is not ProbeMode.AnticipationLead
                and not ProbeMode.AnticipationTransactionJoin)
            return null;

        if (mode is ProbeMode.AnticipationLead or ProbeMode.AnticipationTransactionJoin)
        {
            if (string.IsNullOrWhiteSpace(streamPath)
                || models.Count > 0 || thresholds.Count > 0 || candidates.Count > 0
                || showCases || json || margin != 0.0 || provider != "dml"
                || iterationsSpecified || delaySpecified || maxCharactersSpecified)
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = Array.Empty<ModelSpec>(),
                Margin = 0.0,
                Thresholds = Array.Empty<double>(),
                Candidates = Array.Empty<string>(),
                ShowCases = false,
                Json = false,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
                StreamPath = streamPath,
                StreamBytes = streamBytes,
            };
        }

        if (mode == ProbeMode.CaretContext)
        {
            if (models.Count > 0 || thresholds.Count > 0 || candidates.Count > 0
                || showCases || json || margin != 0.0 || provider != "dml" || iterationsSpecified)
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = Array.Empty<ModelSpec>(),
                Margin = 0.0,
                Thresholds = Array.Empty<double>(),
                Candidates = Array.Empty<string>(),
                ShowCases = false,
                Json = false,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
            };
        }

        if (mode is ProbeMode.SentenceDecisionInventory
            or ProbeMode.SentenceUnanimityBundle)
        {
            if (models.Count > 0 || thresholds.Count > 0 || candidates.Count > 0
                || showCases || json || margin != 0.0 || provider != "dml"
                || iterationsSpecified || delaySpecified || maxCharactersSpecified
                || streamPath is not null || streamBytesSpecified)
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = Array.Empty<ModelSpec>(),
                Margin = 0.0,
                Thresholds = Array.Empty<double>(),
                Candidates = Array.Empty<string>(),
                ShowCases = false,
                Json = false,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
            };
        }

        if (mode == ProbeMode.AutocorrectBenchmark)
        {
            if (models.Count > 0 || thresholds.Count > 0 || candidates.Count > 0
                || showCases || margin != 0.0 || provider != "dml")
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = Array.Empty<ModelSpec>(),
                Margin = 0.0,
                Thresholds = Array.Empty<double>(),
                Candidates = Array.Empty<string>(),
                ShowCases = false,
                Json = json,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
            };
        }

        if (mode == ProbeMode.StaleWork)
        {
            if (models.Count > 0 || thresholds.Count > 0 || candidates.Count > 0
                || showCases || json || margin != 0.0 || provider != "dml"
                || delaySpecified || maxCharactersSpecified)
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = Array.Empty<ModelSpec>(),
                Margin = 0.0,
                Thresholds = Array.Empty<double>(),
                Candidates = Array.Empty<string>(),
                ShowCases = false,
                Json = false,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
            };
        }

        if (mode == ProbeMode.SentenceProfile)
        {
            if (models.Count > 1 || thresholds.Count > 0 || candidates.Count > 0
                || showCases || json || margin != 0.0
                || delaySpecified || maxCharactersSpecified
                || streamPath is not null || streamBytesSpecified)
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = models.Count == 1
                    ? new[] { models[0] }
                    : new[] { ModelPathResolver.DefaultSingleModel() },
                Margin = 0.0,
                Thresholds = Array.Empty<double>(),
                Candidates = Array.Empty<string>(),
                ShowCases = false,
                Json = false,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
            };
        }

        if (mode == ProbeMode.SentenceCalibration)
        {
            if (models.Count > 1 || thresholds.Count > 0 || candidates.Count > 0
                || showCases || json || margin != 0.0 || iterationsSpecified
                || delaySpecified || maxCharactersSpecified
                || streamPath is not null || streamBytesSpecified)
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = models.Count == 1
                    ? new[] { models[0] }
                    : new[] { ModelPathResolver.DefaultSingleModel() },
                Margin = 0.0,
                Thresholds = Array.Empty<double>(),
                Candidates = Array.Empty<string>(),
                ShowCases = false,
                Json = false,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
            };
        }

        if (mode == ProbeMode.SentenceCanonicalLatency)
        {
            if (models.Count > 1 || thresholds.Count > 0 || candidates.Count > 0
                || showCases || json || margin != 0.0 || iterationsSpecified
                || delaySpecified || maxCharactersSpecified
                || streamPath is not null || streamBytesSpecified)
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = models.Count == 1
                    ? new[] { models[0] }
                    : new[] { ModelPathResolver.DefaultSingleModel() },
                Margin = 0.0,
                Thresholds = Array.Empty<double>(),
                Candidates = Array.Empty<string>(),
                ShowCases = false,
                Json = false,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
            };
        }

        if (mode is ProbeMode.SentenceOrderAblation
            or ProbeMode.SentenceBatchTokenization
            or ProbeMode.SentenceBatchExperiment)
        {
            if (models.Count > 1 || thresholds.Count > 0 || candidates.Count > 0
                || showCases || json || margin != 0.0 || iterationsSpecified
                || delaySpecified || maxCharactersSpecified
                || streamPath is not null || streamBytesSpecified)
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = models.Count == 1
                    ? new[] { models[0] }
                    : new[] { ModelPathResolver.DefaultSingleModel() },
                Margin = 0.0,
                Thresholds = Array.Empty<double>(),
                Candidates = Array.Empty<string>(),
                ShowCases = false,
                Json = false,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
            };
        }

        if (mode == ProbeMode.Single)
        {
            if (candidates.Count < 2 || json)
                return null;

            return new ProbeArguments
            {
                Mode = mode,
                Models = models.Count > 0
                    ? new[] { models[^1] }
                    : new[] { ModelPathResolver.DefaultSingleModel() },
                Margin = margin,
                Thresholds = Array.Empty<double>(),
                Candidates = candidates,
                ShowCases = showCases,
                Json = false,
                Provider = provider,
                Iterations = iterations,
                DelaySeconds = delaySeconds,
                MaxCharacters = maxCharacters,
            };
        }

        if (candidates.Count > 0 || json)
            return null;

        return new ProbeArguments
        {
            Mode = mode,
            Models = models.Count > 0 ? models : ModelPathResolver.DefaultBenchmarkModels(),
            Margin = 0.0,
            Thresholds = thresholds.Count > 0
                ? thresholds.Distinct().Order().ToArray()
                : new[] { 0.0, 0.10, 0.25, 0.50, 0.75 },
            Candidates = Array.Empty<string>(),
            ShowCases = showCases,
            Json = false,
            Provider = provider,
            Iterations = iterations,
            DelaySeconds = delaySeconds,
            MaxCharacters = maxCharacters,
        };
    }
}

internal static class ProbeUsage
{
    public static void Print()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --model <dir> [--margin <n>] [--provider <cpu|dml>] --candidate <text> --candidate <text> [...]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --benchmark [--model <label=dir>] [--threshold <n>] [--provider <cpu|dml>] [--show-cases]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --autocorrect-benchmark [--iterations <n>] [--json]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --stale-work-probe [--iterations <n>]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --anticipation-lead-oracle --stream <autocorrect.stream.jsonl> [--stream-bytes <n>]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --anticipation-transaction-join --stream <autocorrect.stream.jsonl> [--stream-bytes <n>]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --sentence-profile [--model <dir>] [--provider <cpu|dml>] [--iterations <rounds>]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --sentence-calibration [--model <dir>] [--provider <cpu|dml>]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --sentence-canonical-latency [--model <dir>] [--provider <cpu|dml>]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --sentence-order-ablation [--model <dir>] [--provider <cpu|dml>]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --sentence-batch-tokenization [--model <dir>] [--provider <cpu|dml>]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --sentence-batch-experiment [--model <dir>] [--provider <cpu|dml>]");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --sentence-decision-inventory");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --sentence-unanimity-bundle");
        Console.Error.WriteLine("  Deckle.Autocorrect.Probe --caret-context [--delay <seconds>] [--max-chars <64..4096>]");
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "--provider selects the ONNX Runtime GenAI execution provider (default dml = GPU/DirectML; cpu = built-in CPU EP).");
        Console.Error.WriteLine(
            "If --model is omitted in single mode, the default is %LOCALAPPDATA%\\Deckle\\models\\qwen3-0.6b-onnx\\onnxruntime\\cpu_and_mobile\\cpu-int4-kld-block-128.");
        Console.Error.WriteLine(
            "If --model is omitted in benchmark mode, staged qwen3-*-onnx models under %LOCALAPPDATA%\\Deckle\\models are used.");
    }
}
