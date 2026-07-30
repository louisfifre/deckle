using System.IO;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The maintainer's domain-pack gestures, expressed as tests like build-data.
// BuildItPack fabricates the pilot computing pack from the kaikki frwiktionary
// dump (fetched by scripts/lib/fetch-autocorrect-data.ps1 -IncludeKaikki) and
// writes its artifact under src/Deckle.Autocorrect/Data/ plus its fabrication
// report and judge worksheet under src/Deckle.Autocorrect.Lab/PackReports/.
// BenchItPackKeyboardQuality then replays the versioned keyboard corpus over
// the effective lexicon (base + pack merged, highest frequency wins) and holds
// it to the same gate as the base: a clean pack must change nothing there —
// the bench exists to prove the sanitization kept masking out.
//
// Both are explicit and skip unless their inputs are present, so an ordinary
// test run never touches the repo or requires the 676 MB dump.
[Trait("Category", "maintenance")]
public sealed class DomainPackMaintenanceTests(ITestOutputHelper output)
{
    private const string DumpFileName = "raw-wiktextract-frwiktionary.jsonl.gz";

    [Fact(Explicit = true)]
    public void BuildItPack()
    {
        string repo = FindRepoRoot();
        string dumpPath = Path.Combine(repo, "artifacts", "autocorrect-data", "raw", DumpFileName);
        string dataDir = Path.Combine(repo, "src", "Deckle.Autocorrect", "Data");
        string reportDir = Path.Combine(repo, "src", "Deckle.Autocorrect.Lab", "PackReports");
        string frenchPath = Path.Combine(dataDir, DataSet.FrenchFile);

        Assert.SkipUnless(
            File.Exists(dumpPath),
            $"Kaikki dump absent at {dumpPath} — run scripts/lib/fetch-autocorrect-data.ps1 -IncludeKaikki first.");
        Assert.SkipUnless(
            File.Exists(frenchPath),
            $"Base French lexicon absent at {frenchPath} — run the build-data gesture first.");

        int code = DomainPackBuilder.Run(
            dumpPath, frenchPath, dataDir, reportDir, DomainPackBuilder.ItPack);
        Assert.Equal(0, code);

        // Self-certify structurally: the pack is non-empty, brings only forms
        // the base lexicon does not carry (fabrication resolved every overlap),
        // and every form sits at or above the flat floor — above only through
        // the versioned promotion overlay.
        var pack = FrequencyLexicon.LoadTsvGz(
            Path.Combine(dataDir, DomainPackBuilder.ItPack.FileName));
        var french = FrequencyLexicon.LoadTsvGz(frenchPath);
        Assert.True(pack.Count > 0, "The pilot pack came out empty.");
        int promoted = 0;
        foreach (var (form, freq) in pack.Entries)
        {
            Assert.False(french.Contains(form), $"Pack form '{form}' already lives in the base lexicon.");
            Assert.True(freq >= DomainPackBuilder.FloorFrequencyPerMillion,
                $"Pack form '{form}' sits below the floor frequency ({freq}).");
            if (freq > DomainPackBuilder.FloorFrequencyPerMillion)
                promoted++;
        }
        output.WriteLine($"pack fr-it: {pack.Count} forms, {promoted} promoted above "
            + $"the {DomainPackBuilder.FloorFrequencyPerMillion} opm floor");
    }

    [Fact(Explicit = true)]
    public void BenchItPackKeyboardQuality()
    {
        string repo = FindRepoRoot();
        string dataDir = Path.Combine(repo, "src", "Deckle.Autocorrect", "Data");
        string packPath = Path.Combine(dataDir, DomainPackBuilder.ItPack.FileName);

        Assert.SkipUnless(
            File.Exists(packPath),
            $"Pilot pack absent at {packPath} — run BuildItPack first.");

        string effectiveDir = BuildEffectiveDataDir(dataDir, packPath, DomainPackBuilder.ItPack.Key);

        KeyboardQualitySummary baseline = AutocorrectBenchmark.MeasureKeyboardQuality(dataDir);
        KeyboardQualitySummary effective = AutocorrectBenchmark.MeasureKeyboardQuality(effectiveDir);

        string score =
            $"baseline:  internal_edit_pair_precision={baseline.InternalEditPairPrecision:P1} recall={baseline.Recall:P1} "
            + $"exact={baseline.ExactRate:P1} wrong={baseline.WrongChanges}\n"
            + $"effective: internal_edit_pair_precision={effective.InternalEditPairPrecision:P1} recall={effective.Recall:P1} "
            + $"exact={effective.ExactRate:P1} wrong={effective.WrongChanges}";
        output.WriteLine(score);
        foreach (string failure in effective.Failures)
            output.WriteLine(failure);

        string detail = score + Environment.NewLine
            + string.Join(Environment.NewLine, effective.Failures);
        Assert.True(effective.WrongChanges == 0, detail);
        Assert.True(effective.Recall >= baseline.Recall, detail);
        Assert.True(effective.ExactRate >= baseline.ExactRate, detail);
    }

    // Replays the IT-pack gold corpus — sentences that actually type pack
    // vocabulary — over the base lexicon (control), the effective lexicon
    // (base + pack at the flat floor), and, when the experiment file is
    // present, a frequency-promoted pack variant
    // (artifacts/autocorrect-data/experiments/pack-fr-it-wordfreq.tsv.gz).
    // The gate on the effective run: zero wrong changes, and no failure
    // outside the known residue — the contested Morphalou-epsilon fold and
    // the instant typo repair toward a sub-bar pack form, both measured and
    // journaled when the floor-vs-promotion call was made. Everything else
    // (protection, sole-candidate restoration, promoted contests) must pass.
    [Fact(Explicit = true)]
    public void BenchItPackVocabulary()
    {
        string repo = FindRepoRoot();
        string dataDir = Path.Combine(repo, "src", "Deckle.Autocorrect", "Data");
        string packPath = Path.Combine(dataDir, DomainPackBuilder.ItPack.FileName);

        Assert.SkipUnless(
            File.Exists(packPath),
            $"Pilot pack absent at {packPath} — run BuildItPack first.");

        KeyboardQualitySummary control =
            AutocorrectBenchmark.MeasureKeyboardQuality(dataDir, AutocorrectItPackCorpus.All);
        Report("base (control)", control);

        string effectiveDir = BuildEffectiveDataDir(dataDir, packPath, "it-shipped");
        KeyboardQualitySummary effective =
            AutocorrectBenchmark.MeasureKeyboardQuality(effectiveDir, AutocorrectItPackCorpus.All);
        Report("effective (shipped pack)", effective);

        string promotedPack = Path.Combine(
            repo, "artifacts", "autocorrect-data", "experiments", "pack-fr-it-wordfreq.tsv.gz");
        if (File.Exists(promotedPack))
        {
            string promotedDir = BuildEffectiveDataDir(dataDir, promotedPack, "it-wordfreq");
            Report("effective (wordfreq)",
                AutocorrectBenchmark.MeasureKeyboardQuality(promotedDir, AutocorrectItPackCorpus.All));
            // The promotion's harm side: the general corpus must stay clean
            // when pack frequencies rise.
            Report("general corpus (wordfreq)",
                AutocorrectBenchmark.MeasureKeyboardQuality(promotedDir));
        }
        else
        {
            output.WriteLine($"(no promoted variant at {promotedPack} — floor only)");
        }

        string[] knownResidue =
        [
            "restore_contested_reinitialise",
            "repair_transposition_toward_pack",
        ];
        string detail = string.Join(Environment.NewLine, effective.Failures);
        Assert.True(effective.WrongChanges == 0, detail);
        Assert.False(
            effective.Failures.Any(failure =>
                !knownResidue.Any(residue =>
                    failure.StartsWith(residue, StringComparison.Ordinal))),
            detail);
    }

    private void Report(string label, KeyboardQualitySummary summary)
    {
        output.WriteLine(
            $"{label}: internal_edit_pair_precision={summary.InternalEditPairPrecision:P1} recall={summary.Recall:P1} "
            + $"({summary.TrueChanges}/{summary.GoldChanges}) exact={summary.ExactRate:P1} "
            + $"wrong={summary.WrongChanges}");
        foreach (string failure in summary.Failures)
            output.WriteLine($"  {failure}");
    }

    // The effective data dir: every artifact as shipped, except the French
    // lexicon replaced by the base+pack max-wins merge the runtime would use.
    private static string BuildEffectiveDataDir(string dataDir, string packPath, string variant)
    {
        string effectiveDir = Path.Combine(
            Path.GetTempPath(), "deckle-domain-pack-bench", variant);
        if (Directory.Exists(effectiveDir))
            Directory.Delete(effectiveDir, recursive: true);
        Directory.CreateDirectory(effectiveDir);
        foreach (string file in Directory.GetFiles(dataDir))
            File.Copy(file, Path.Combine(effectiveDir, Path.GetFileName(file)));
        DomainPackBuilder.MergeEffective(
            Path.Combine(dataDir, DataSet.FrenchFile),
            packPath,
            Path.Combine(effectiveDir, DataSet.FrenchFile));
        return effectiveDir;
    }

    // Walk up from the test binary to the repo root, marked by the central package
    // props that live only there — worktree-safe (no hardcoded path).
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        Assert.SkipWhen(dir is null, "Could not locate the repo root (Directory.Packages.props).");
        return dir!.FullName;
    }
}
