using System.IO;
using System.Text;
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

        // Context disambiguator tuning. Margin and evidence are exposed for the
        // sensitivity sweep; unset flags inherit the record defaults so the eval
        // always measures what the live engine would do.
        var recordDefaults = new DisambiguatorOptions();
        var contextOptions = new DisambiguatorOptions
        {
            MarginRatio = args.DoubleOr("--margin", recordDefaults.MarginRatio),
            MinEvidence = args.IntOr("--evidence", recordDefaults.MinEvidence),
        };

        var data = DataSet.Load(
            dataDir,
            wantEnglish: !noEnglish,
            wantContext: !noContext,
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
                        + $" evidence {contextOptions.MinEvidence})");
        Console.WriteLine($"Dominance   : {options.DominanceRatio:0.###}x");
        Console.WriteLine($"Valid forms : {options.CorrectValidFormsWithContext}");
        Console.WriteLine();

        RestorationReport report;
        using (var reader = new StreamReader(corpus, Encoding.UTF8))
            report = RestorationEvaluator.Evaluate(reader, restorer, evalOptions);

        Console.Write(report.FormatConsole());
        return 0;
    }
}
