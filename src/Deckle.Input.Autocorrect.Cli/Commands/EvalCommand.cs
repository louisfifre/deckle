using System.IO;
using System.Text;
using Deckle.Input.Autocorrect.Cli.Mlm;
using Deckle.Input.Autocorrect.Engine;
using Deckle.Input.Autocorrect.Evaluation;

namespace Deckle.Input.Autocorrect.Cli.Commands;

// Runs the offline restoration eval against the wiki-fr-eval reference. The
// flags compose the exact policy under test — lexical gate alone, plus the
// English guard, plus the context pair model — so the eval matrix can isolate
// what each stage buys and what it breaks. Prints the report's own table.
internal static class EvalCommand
{
    public static int Run(CliArgs args)
    {
        string root = RepoPaths.RepoRoot();
        string dataDir = args.ValueOr("--data", RepoPaths.DefaultDataDir(root));
        string corpus = args.ValueOr("--corpus",
            Path.Combine(RepoPaths.DefaultRawDir(root), "wiki-fr-eval.txt"));

        bool noContext = args.Has("--no-context");
        bool noEnglish = args.Has("--no-en");
        // The post-sentence CamemBERT reranker IS the context stage when on, so
        // the left-context bigram is dropped — we measure gate + reranker.
        bool useReranker = args.Has("--reranker");

        // Context disambiguator tuning. Margin and evidence are exposed for the
        // sensitivity sweep; unset flags inherit the record defaults so the eval
        // always measures what the live engine would do.
        var recordDefaults = new DisambiguatorOptions();
        var contextOptions = new DisambiguatorOptions
        {
            MarginRatio = args.DoubleOr("--margin", recordDefaults.MarginRatio),
            MinEvidence = args.IntOr("--evidence", recordDefaults.MinEvidence),
            MaxContextOrder = args.IntOr("--max-order", recordDefaults.MaxContextOrder),
        };

        var data = DataSet.Load(
            dataDir,
            wantEnglish: !noEnglish,
            wantContext: !noContext && !useReranker,
            contextOptions: contextOptions);
        if (data is null) return 1;

        if (!File.Exists(corpus))
        {
            Console.Error.WriteLine($"Missing corpus: {corpus}");
            return 1;
        }

        var options = new RestorerOptions
        {
            EnglishGuardMinPerMillion = args.DoubleOr("--en-guard", 5.0),
            DominanceRatio = args.DoubleOr("--dominance", 20.0),
            CorrectValidFormsWithContext = args.Has("--valid-forms"),
            MinCandidateFrequencyPerMillion = args.DoubleOr("--min-cand-freq", 0.0),
            GuardCapitalizedMidSentence = args.Has("--guard-caps"),
        };

        var restorer = new DiacriticsRestorer(
            french: data.French,
            english: data.English,
            index: data.Index,
            options: options,
            context: data.Context);

        var evalOptions = new EvaluatorOptions { MaxTokens = args.IntOr("--max-tokens", 0) };

        // Echo the composed configuration — the matrix runs read identically.
        Console.WriteLine($"Corpus      : {corpus}");
        Console.WriteLine($"English     : {(data.English is not null ? "on" : "off")}"
                        + $"  (guard {options.EnglishGuardMinPerMillion:0.###} ppm)");
        Console.WriteLine($"Context     : {(data.Context is not null ? "on" : "off")}"
                        + $"  (margin {contextOptions.MarginRatio:0.###}x,"
                        + $" evidence {contextOptions.MinEvidence},"
                        + $" order {contextOptions.MaxContextOrder})");
        Console.WriteLine($"Dominance   : {options.DominanceRatio:0.###}x");
        Console.WriteLine($"Cand. floor : {options.MinCandidateFrequencyPerMillion:0.###} ppm");
        Console.WriteLine($"Caps guard  : {options.GuardCapitalizedMidSentence}");
        Console.WriteLine($"Valid forms : {options.CorrectValidFormsWithContext}");

        RestorationReport report;
        if (useReranker)
        {
            string modelDir = args.ValueOr("--model",
                Path.Combine(RepoPaths.DefaultRawDir(root), "..", "models", "camembert-base"));
            double margin = args.DoubleOr("--rerank-margin", 2.0);
            double freqPrior = args.DoubleOr("--rerank-freq-prior", 1.0);
            if (!File.Exists(Path.Combine(modelDir, "model.onnx")))
            {
                Console.Error.WriteLine($"Missing model: {Path.Combine(modelDir, "model.onnx")}");
                return 1;
            }
            Console.WriteLine($"Reranker    : on   (CamemBERT MLM, margin {margin:0.###}, freq-prior {freqPrior:0.###})");
            Console.WriteLine();

            using var reranker = new CamembertSentenceReranker(modelDir, margin, freqPrior);
            using var reader = new StreamReader(corpus, Encoding.UTF8);
            // The restorer is both the gate (ICorrectionPolicy) and the ambiguity
            // probe (IAmbiguityProbe) — it knows which slots it left for context.
            report = RestorationEvaluator.EvaluateReranked(reader, restorer, restorer, reranker, evalOptions);
        }
        else
        {
            Console.WriteLine();
            using var reader = new StreamReader(corpus, Encoding.UTF8);
            report = RestorationEvaluator.Evaluate(reader, restorer, evalOptions);
        }

        Console.Write(report.FormatConsole());
        return 0;
    }
}
