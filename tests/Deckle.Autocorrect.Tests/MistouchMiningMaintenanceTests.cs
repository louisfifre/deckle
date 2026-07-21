using System.IO;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The maintainer's mining gesture: mine mistouch families off the collected
// typed-sentence corpus and write the review artifacts next to it — the markdown
// the maintainer reads (the one-time review gate before family adoption turns
// automatic) and the JSON the routing wiring will consume once reviewed. Wires
// the REAL lexicons like the replay gesture; read-only over the corpus, no
// model, seconds. A silent skip when corpus or lexicon is absent.
[Trait("Category", "maintenance")]
public sealed class MistouchMiningMaintenanceTests
{
    private readonly ITestOutputHelper _out;

    public MistouchMiningMaintenanceTests(ITestOutputHelper output) => _out = output;

    private static string? FindCorpus() => new[]
    {
        Path.Combine(AppPaths.TelemetryDirectory, "validation", "autocorrect.text.jsonl"),
        Path.Combine(AppPaths.TelemetryDirectory, "autocorrect.text.jsonl"),
    }.FirstOrDefault(File.Exists);

    [Fact(Explicit = true)]
    public void MinesMistouchFamiliesOverTheCollectedCorpus()
    {
        string? corpusPath = FindCorpus();
        Assert.SkipUnless(corpusPath is not null, "no typed-text corpus collected yet");

        string dataDir = Path.Combine(System.AppContext.BaseDirectory, "Data");
        string frenchPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.FrenchFileName);
        Assert.SkipUnless(File.Exists(frenchPath), "packaged FR lexicon absent");

        var french = FrequencyLexicon.LoadTsvGz(frenchPath);
        var english = AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir);

        var entries = CorpusReader.Read(corpusPath!).ToList();
        Assert.SkipUnless(entries.Count > 0, "the corpus holds no parseable record yet");

        MistouchMiner.MiningResult result = MistouchMiner.Mine(entries, french, english);

        string directory = Path.GetDirectoryName(corpusPath!)!;
        string mdPath = Path.Combine(directory, "autocorrect.mistouch-families.md");
        string jsonPath = Path.Combine(directory, "autocorrect.mistouch-families.json");
        File.WriteAllText(mdPath, MistouchFamilyReport.RenderMarkdown(result));
        File.WriteAllText(jsonPath, MistouchFamilyReport.RenderJson(result));

        _out.WriteLine(
            $"{result.Families.Count} families ({result.Families.Sum(f => f.Evidence)} evidence) "
            + $"over {entries.Count} records; {result.Unclassified.Count} unclassified repairs.");
        _out.WriteLine($"Review artifact → {mdPath}");
        _out.WriteLine($"Routing artifact → {jsonPath}");
    }
}
