using System.IO;
using Deckle.Input.Autocorrect.Cli.Mlm;
using Deckle.Input.Autocorrect.Engine;
using Deckle.Input.Autocorrect.Evaluation;

namespace Deckle.Input.Autocorrect.Cli.Commands;

// The dry-run probe: feeds text through the live policy (lexical gate + the
// post-sentence CamemBERT reranker) and prints what it WOULD correct — never
// injects, never touches a buffer. This is the fast iteration loop and the
// manual adversarial test: throw it bilingual text, code, proper nouns — the
// failure modes the monolingual Wikipedia corpus cannot surface.
//
// Positional text runs once; no text opens a REPL (one line per turn, empty
// line or Ctrl+Z to quit), so the heavy model loads once and every line after
// is cheap. `--strip` simulates a typist from accented text you paste.
internal static class DryRunCommand
{
    public static int Run(CliArgs args)
    {
        string root = RepoPaths.RepoRoot();
        string dataDir = args.ValueOr("--data", RepoPaths.DefaultDataDir(root));
        bool noEnglish = args.Has("--no-en");
        bool strip = args.Has("--strip");

        // The reranker IS the context stage, so no bigram is loaded.
        var data = DataSet.Load(dataDir, wantEnglish: !noEnglish, wantContext: false);
        if (data is null) return 1;

        var options = new RestorerOptions
        {
            EnglishGuardMinPerMillion = args.DoubleOr("--en-guard", 5.0),
            DominanceRatio = args.DoubleOr("--dominance", 20.0),
        };
        var restorer = new DiacriticsRestorer(data.French, data.English, data.Index, options);

        string modelDir = args.ValueOr("--model",
            Path.Combine(RepoPaths.DefaultRawDir(root), "..", "models", "camembert-base"));
        if (!File.Exists(Path.Combine(modelDir, "model.onnx")))
        {
            Console.Error.WriteLine($"Missing model: {Path.Combine(modelDir, "model.onnx")}");
            return 1;
        }
        double margin = args.DoubleOr("--rerank-margin", 2.0);
        double freqPrior = args.DoubleOr("--rerank-freq-prior", 1.0);
        // By default only corrections are shown; left-as-typed words are the
        // norm, not a failure. --show-ambiguous reveals the slots the engine
        // saw as ambiguous and deliberately left alone (the conservative call).
        bool showAmbiguous = args.Has("--show-ambiguous");

        Console.WriteLine($"Model    : {Path.GetFullPath(modelDir)}");
        Console.WriteLine($"Reranker : margin {margin:0.###}, freq-prior {freqPrior:0.###}");
        Console.WriteLine($"English  : {(data.English is not null ? $"guard {options.EnglishGuardMinPerMillion:0.###} ppm" : "off")}");
        Console.WriteLine($"Strip    : {(strip ? "on  (simulate a typist from accented text)" : "off (text taken as typed)")}");
        Console.WriteLine();

        using var reranker = new CamembertSentenceReranker(modelDir, margin, freqPrior);

        // Positional tokens (everything not a --flag) form a one-shot line.
        string inline = string.Join(' ', args.Positional);
        if (!string.IsNullOrWhiteSpace(inline))
        {
            Render(inline, restorer, restorer, reranker, strip, showAmbiguous);
            return 0;
        }

        Console.WriteLine("Type a line to test (empty line or Ctrl+Z to quit):");
        while (true)
        {
            Console.Write("> ");
            string? line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
                break;
            Render(line, restorer, restorer, reranker, strip, showAmbiguous);
        }
        return 0;
    }

    // One line's verdict. By default it lists only what the engine WOULD change
    // (typed → output, with the stage) — leaving a word untouched is the normal,
    // correct case for the vast majority of words ("du", "des", "a"-the-verb…),
    // not something to flag. The summary names what changed; --show-ambiguous
    // additionally surfaces the slots seen as ambiguous and left as typed, so a
    // genuine miss can be told apart from a correct hold.
    private static void Render(
        string line, ICorrectionPolicy policy, IAmbiguityProbe probe,
        ISentenceReranker reranker, bool strip, bool showAmbiguous)
    {
        var outcomes = RestorationEvaluator.RestoreLine(line, policy, probe, reranker, strip);

        int corrected = 0, ambiguous = 0;
        foreach (var o in outcomes)
        {
            if (!string.Equals(o.Typed, o.Output, StringComparison.Ordinal))
            {
                corrected++;
                Console.WriteLine($"  {o.Typed,-20} → {o.Output,-20} {o.Reason}");
            }
            else if (o.WasAmbiguous)
            {
                ambiguous++;
                if (showAmbiguous)
                    Console.WriteLine($"  {o.Typed,-20}   (ambiguous — left as typed)");
            }
        }

        if (corrected == 0)
            Console.WriteLine("  (nothing to correct — left as typed)");
        string tail = ambiguous > 0 ? $" · {ambiguous} ambiguous left as typed" : "";
        Console.WriteLine($"  — {corrected} correction(s){tail}");
        Console.WriteLine();
    }
}
