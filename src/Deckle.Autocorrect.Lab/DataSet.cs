using System.IO;

namespace Deckle.Autocorrect.Lab;

internal static class DataSet
{
    public const string FrenchFile = "lexicon-fr.tsv.gz";
    public const string EnglishFile = "lexicon-en.tsv.gz";
    public const string PairFile = "pair-bigrams-fr.tsv.gz";

    public static string FrenchPath(string dataDir) => Path.Combine(dataDir, FrenchFile);
    public static string EnglishPath(string dataDir) => Path.Combine(dataDir, EnglishFile);
    public static string PairPath(string dataDir) => Path.Combine(dataDir, PairFile);
}
