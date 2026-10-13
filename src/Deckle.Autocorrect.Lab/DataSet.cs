using System.IO;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

internal sealed class DataSet
{
    public const string FrenchFile = "lexicon-fr.tsv.gz";
    public const string EnglishFile = "lexicon-en.tsv.gz";
    public const string PairFile = "pair-bigrams-fr.tsv.gz";
    public const string VerbsFile = "verbs-fr.tsv.gz";

    public required FrequencyLexicon French { get; init; }
    public FrequencyLexicon? English { get; init; }
    public required AccentIndex Index { get; init; }
    public BigramPairDisambiguator? Context { get; init; }

    public static string FrenchPath(string dataDir) => Path.Combine(dataDir, FrenchFile);
    public static string EnglishPath(string dataDir) => Path.Combine(dataDir, EnglishFile);
    public static string PairPath(string dataDir) => Path.Combine(dataDir, PairFile);
    public static string VerbsPath(string dataDir) => Path.Combine(dataDir, VerbsFile);

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
