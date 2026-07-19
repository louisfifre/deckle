using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// Regenerates the derived lexicons from the raw sources. French from
// Lexique 3.83 (form + film/book frequencies), legacy English from
// Norvig's count_1w, and the restricted globish seed from FranceTerme
// English equivalents. They land as gzip TSVs, ordinally sorted so the
// artifacts are byte-deterministic across runs and machines.
public static partial class LexiconBuilder
{
    // Frequency stamped on a Morphalou-only form (one Lexique does not carry).
    // Not zero, on purpose: a zero runner-up would break the dominance gate
    // (which requires the runner-up > 0), so a Morphalou form folded next to a
    // real one would silently suppress a valid restoration. Epsilon keeps the
    // dominance maths intact while sitting far below MinDominantFrequency, so a
    // Morphalou-only form never wins a contested slot on its own — it only ever
    // restores when it is the SOLE candidate of a fold (the lexical gate fires
    // on a single candidate regardless of frequency).
    private const double MorphalouEpsilonPerMillion = 0.001;

    private static readonly HashSet<string> GlobishStopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from",
        "in", "into", "is", "it", "its", "of", "on", "or", "the", "to",
        "with",
    };

    // Builds the lexicons. The caller supplies the resolved directories — rawDir
    // holds the fetched sources (Lexique383.tsv, count_1w.txt, optionally
    // Morphalou3.1_CSV.csv and FranceTerme.xml), outDir is where the gzip
    // TSV artifacts are written.
    // withMorphalou folds in the inflected-form coverage when its source is
    // present. Returns 0 on success.
    public static int Run(string rawDir, string outDir, bool withMorphalou = false)
    {
        Directory.CreateDirectory(outDir);

        string frenchOut = DataSet.FrenchPath(outDir);
        string englishOut = DataSet.EnglishPath(outDir);
        string globishOut = Path.Combine(outDir, AutocorrectLexiconArtifacts.GlobalEnglishSeedFileName);
        string verbsOut = DataSet.VerbsPath(outDir);

        Console.WriteLine($"Raw  : {rawDir}");
        Console.WriteLine($"Out  : {outDir}");
        Console.WriteLine();

        // Morphalou overlay is opt-in: the default lexicon is Lexique-only and
        // byte-deterministic. withMorphalou folds in the inflected-form coverage
        // once the source has been fetched (see fetch-autocorrect-data.ps1).
        string morphalouSource = withMorphalou
            ? Path.Combine(rawDir, "Morphalou3.1_CSV.csv")
            : "";
        if (withMorphalou && !File.Exists(morphalouSource))
            Console.WriteLine("Note: withMorphalou set but Morphalou3.1_CSV.csv is absent — "
                            + "run fetch-autocorrect-data.ps1 first. Building Lexique-only.");

        BuildFrench(Path.Combine(rawDir, "Lexique383.tsv"), morphalouSource, frenchOut, verbsOut);
        BuildEnglish(Path.Combine(rawDir, "count_1w.txt"), englishOut);

        var french = FrequencyLexicon.LoadTsvGz(frenchOut);
        BuildGlobalEnglishSeed(Path.Combine(rawDir, "FranceTerme.xml"), globishOut, french);

        SelfCheck(frenchOut, englishOut, verbsOut, globishOut);

        return 0;
    }
}
