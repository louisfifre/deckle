using System.IO;
using System.Text;
using Deckle.Input.Autocorrect.Engine;
using Deckle.Input.Autocorrect.Lexicon;

namespace Deckle.Input.Autocorrect.Cli.Commands;

// Trains the left-context pair model over the French Wikipedia corpus and
// writes it next to the lexicons. The trainer's notion of "ambiguous" comes
// from the same French lexicon + index the runtime gate uses, so the model
// learns the slots the engine will actually query.
internal static class TrainPairsCommand
{
    public static int Run(CliArgs args)
    {
        string root = RepoPaths.RepoRoot();
        string dataDir = args.ValueOr("--data", RepoPaths.DefaultDataDir(root));
        string corpus = args.ValueOr("--corpus",
            Path.Combine(RepoPaths.DefaultRawDir(root), "wiki-fr-train.txt"));

        string frenchPath = DataSet.FrenchPath(dataDir);
        if (!File.Exists(frenchPath))
        {
            Console.Error.WriteLine($"Missing artifact: {frenchPath}");
            Console.Error.WriteLine("Run `build-data` first.");
            return 1;
        }
        if (!File.Exists(corpus))
        {
            Console.Error.WriteLine($"Missing corpus: {corpus}");
            return 1;
        }

        Console.WriteLine($"Corpus: {corpus}");
        Console.WriteLine($"Data  : {dataDir}");
        Console.WriteLine();

        var french = FrequencyLexicon.LoadTsvGz(frenchPath);
        var index = AccentIndex.Build(french);

        var trainerOptions = new TrainerOptions
        {
            MaxOrder = args.IntOr("--max-order", new TrainerOptions().MaxOrder),
        };

        string outPath = DataSet.PairPath(dataDir);
        TrainerReport report;
        using (var reader = new StreamReader(corpus, Encoding.UTF8))
            report = PairModelTrainer.TrainToFile(reader, french, index, outPath, trainerOptions);

        // Reload to report the on-disk model's shape (the rows that survived).
        var model = PairModel.LoadTsvGz(outPath);

        Console.WriteLine("Trainer report:");
        Console.WriteLine($"  sentences               {report.Sentences,12:N0}");
        Console.WriteLine($"  tokens                  {report.Tokens,12:N0}");
        Console.WriteLine($"  ambiguous occurrences   {report.AmbiguousSlotOccurrences,12:N0}");
        Console.WriteLine($"  kept rows               {report.KeptRows,12:N0}");
        Console.WriteLine();
        Console.WriteLine($"  model slot count        {model.SlotCount,12:N0}");
        Console.WriteLine($"  model row count         {model.RowCount,12:N0}");
        Console.WriteLine();
        Console.WriteLine($"File: {Path.GetFileName(outPath)}  {new FileInfo(outPath).Length:N0} bytes");
        return 0;
    }
}
