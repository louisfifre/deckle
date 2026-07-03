using System.IO;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Deckle.Autocorrect.Onnx;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The maintainer's replay gesture: run the collected typed-text corpus through the
// staged ONNX judge, offline, and write the margin-calibration report. It is not a
// unit test — it wires the REAL FR lexicon and the REAL model, and is a silent skip
// wherever either is absent (CI, a fresh clone, before any corpus is collected or a
// judge is staged). Where both are present it is a deliberate, supervised run: the
// judge is seconds per slot, so a full corpus is minutes, and nothing is applied.
[Trait("Category", "gesture")]
public sealed class SentenceReplayGestureTests
{
    private readonly ITestOutputHelper _out;

    public SentenceReplayGestureTests(ITestOutputHelper output) => _out = output;

    // The corpus lands either directly under telemetry/ or in its validation
    // subfolder, depending on the sink's configuration — take whichever exists.
    private static string? FindCorpus() => new[]
    {
        Path.Combine(AppPaths.TelemetryDirectory, "validation", "autocorrect.text.jsonl"),
        Path.Combine(AppPaths.TelemetryDirectory, "autocorrect.text.jsonl"),
    }.FirstOrDefault(File.Exists);

    [Fact]
    public void CalibratesTheSentenceMarginOverTheCollectedCorpus()
    {
        string? corpusPath = FindCorpus();
        Assert.SkipUnless(corpusPath is not null, "no typed-text corpus collected yet");

        // The packaged FR lexicon drives the ambiguity probe — the same artifact
        // the live engine loads, so the replay sees the engine's candidate sets.
        string dataDir = Path.Combine(System.AppContext.BaseDirectory, "Data");
        string frenchPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.FrenchFileName);
        Assert.SkipUnless(File.Exists(frenchPath), "packaged FR lexicon absent");

        // The judge is role-named on disk, not model-named: whatever ORT GenAI
        // export is staged there (the Luth-vs-Qwen choice is still open) is picked
        // up. Skip when absent; once staged, a broken export must fail loudly.
        string judgeDir = Path.Combine(AppPaths.ModelsDirectory, "sentence-judge");
        Assert.SkipUnless(Directory.Exists(judgeDir), "sentence judge not staged");

        var french = FrequencyLexicon.LoadTsvGz(frenchPath);
        var probe = new DiacriticsRestorer(french, english: null, AccentIndex.Build(french));

        // margin 0 → the judge returns its raw argmax and gap for every slot, so
        // the sweep, not the model, sets the operating margin.
        using OnnxSlotReranker? judge = OnnxSlotReranker.TryLoad(judgeDir, margin: 0.0);
        Assert.NotNull(judge);

        ReplayReport report = ReplayRunner.Run(corpusPath!, probe, judge!);

        string reportPath = Path.Combine(Path.GetDirectoryName(corpusPath!)!, "autocorrect.replay-calibration.md");
        File.WriteAllText(reportPath, report.Markdown);

        _out.WriteLine($"{report.Summary.AmbiguousSlots} ambiguous slots judged over {report.Summary.Sentences} sentences.");
        _out.WriteLine($"Calibration report → {reportPath}");

        Assert.NotEmpty(report.Slots);
    }
}
