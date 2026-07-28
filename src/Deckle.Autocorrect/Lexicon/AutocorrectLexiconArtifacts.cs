using System.IO;

namespace Deckle.Autocorrect;

// File names and loading rules for the lexicons consumed by the live engine.
// The global-English tier intentionally keys off the restricted seed artifact:
// the historical full English list may still exist in Data/, but it is not a
// protected-literal tier for the product.
public static class AutocorrectLexiconArtifacts
{
    public const string FrenchFileName = "lexicon-fr.tsv.gz";
    public const string PairBigramsFrenchFileName = "pair-bigrams-fr.tsv.gz";
    public const string VerbMorphologyFrenchFileName = "verbs-fr.tsv.gz";
    public const string GlobalEnglishSeedFileName = "lexicon-en-globish.tsv.gz";

    // Where the derived artifacts land beside the executable (Deckle.App's
    // FlattenAutocorrectLexicons target puts them there). One source, because
    // the engine build is no longer the only reader: the settings page reads
    // the packs' manifests from the same place.
    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Data");

    public static FrequencyLexicon? LoadGlobalEnglishSeed(string dataDir)
    {
        string path = Path.Combine(dataDir, GlobalEnglishSeedFileName);
        return File.Exists(path) ? FrequencyLexicon.LoadTsvGz(path) : null;
    }
}
