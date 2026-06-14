using System.IO;
using Deckle.Input.Autocorrect;

namespace Deckle.Input.Autocorrect.Cli;

// The derived artifacts the engine reads, loaded from a Data/ directory:
// the French lexicon + its accent index (mandatory), the English guard
// lexicon (optional), and the context pair model (optional, present only
// after train-pairs). The static names mirror what build-data / train-pairs
// write, so the host and the artifacts agree by convention, not config.
internal sealed class DataSet
{
    public const string FrenchFile = "lexicon-fr.tsv.gz";
    public const string EnglishFile = "lexicon-en.tsv.gz";
    public const string PairFile = "pair-bigrams-fr.tsv.gz";

    public required FrequencyLexicon French { get; init; }
    public FrequencyLexicon? English { get; init; }
    public required AccentIndex Index { get; init; }
    public BigramPairDisambiguator? Context { get; init; }

    public static string FrenchPath(string dataDir) => Path.Combine(dataDir, FrenchFile);
    public static string EnglishPath(string dataDir) => Path.Combine(dataDir, EnglishFile);
    public static string PairPath(string dataDir) => Path.Combine(dataDir, PairFile);

    // Loads the dataset. English and the pair model are pulled only when their
    // files exist and the caller asks for them. The pair model carries the
    // caller's disambiguator tuning (margin, evidence, bias); null leaves the
    // record defaults. Returns null (after printing a pointer to build-data)
    // when the mandatory French artifact is missing.
    public static DataSet? Load(
        string dataDir,
        bool wantEnglish,
        bool wantContext,
        DisambiguatorOptions? contextOptions = null)
    {
        string frenchPath = FrenchPath(dataDir);
        if (!File.Exists(frenchPath))
        {
            Console.Error.WriteLine($"Missing artifact: {frenchPath}");
            Console.Error.WriteLine("Run `build-data` first to generate the lexicons.");
            return null;
        }

        var french = FrequencyLexicon.LoadTsvGz(frenchPath);

        FrequencyLexicon? english = null;
        if (wantEnglish && File.Exists(EnglishPath(dataDir)))
            english = FrequencyLexicon.LoadTsvGz(EnglishPath(dataDir));

        BigramPairDisambiguator? context = null;
        if (wantContext && File.Exists(PairPath(dataDir)))
            context = BigramPairDisambiguator.LoadTsvGz(PairPath(dataDir), contextOptions);

        return new DataSet
        {
            French = french,
            English = english,
            Index = AccentIndex.Build(french),
            Context = context,
        };
    }
}
