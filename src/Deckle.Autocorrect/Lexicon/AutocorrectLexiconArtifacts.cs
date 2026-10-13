using System.IO;

namespace Deckle.Autocorrect;

// File names and loading rules for the lexicons consumed by the live engine.
// The global-English tier intentionally keys off the restricted seed artifact:
// the full English list (lexicon-en.tsv.gz) also sits in Data/, but it is not
// a protected-literal tier for the product.
public static class AutocorrectLexiconArtifacts
{
    public const string FrenchFileName = "lexicon-fr.tsv.gz";
    public const string PairBigramsFrenchFileName = "pair-bigrams-fr.tsv.gz";
    public const string VerbMorphologyFrenchFileName = "verbs-fr.tsv.gz";
    public const string GlobalEnglishSeedFileName = "lexicon-en-globish.tsv.gz";

    // Deckle.App's FlattenAutocorrectLexicons target puts the derived artifacts
    // beside the executable. The engine build and the settings page (the packs'
    // manifests) both read from here.
    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Data");

    public static FrequencyLexicon? LoadGlobalEnglishSeed(string dataDir)
    {
        string path = Path.Combine(dataDir, GlobalEnglishSeedFileName);
        return File.Exists(path) ? FrequencyLexicon.LoadTsvGz(path) : null;
    }
}
