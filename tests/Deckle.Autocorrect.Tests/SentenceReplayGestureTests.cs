using System;
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

        // The execution provider is overridable so the same gesture runs the judge on
        // the GPU (DirectML, the default) or falls back to the CPU EP for a comparison,
        // without a recompile.
        string ep = Environment.GetEnvironmentVariable("DECKLE_ONNX_JUDGE_EP") is { Length: > 0 } value
            ? value
            : "dml";
        Console.Error.WriteLine($"[replay] loading judge — ep={ep}, dir={judgeDir}");

        // margin 0 → the judge returns its raw argmax and gap for every slot, so
        // the sweep, not the model, sets the operating margin.
        using OnnxSlotReranker? judge = OnnxSlotReranker.TryLoad(judgeDir, margin: 0.0, executionProvider: ep);
        Assert.NotNull(judge);
        Console.Error.WriteLine("[replay] judge loaded — replaying corpus, nothing is applied");

        // The maintainer's truth sheet sits next to the corpus. The file-based Run
        // has already read its resolved cells and measured agreement against them;
        // here the sheet is regenerated from this pass's disagreements and MERGED
        // with the existing one, so a filled truth cell survives corpus growth.
        string sheetPath = TruthOverlay.SheetPathFor(corpusPath!);
        var existingSheet = TruthOverlay.Read(sheetPath);

        ReplayReport report = ReplayRunner.Run(corpusPath!, probe, judge!, onProgress: OnReplayProgress);

        string reportPath = Path.Combine(Path.GetDirectoryName(corpusPath!)!, "autocorrect.replay-calibration.md");
        File.WriteAllText(reportPath, report.Markdown);
        File.WriteAllText(sheetPath, TruthOverlay.Render(TruthOverlay.Merge(report.TruthReview, existingSheet)));

        Console.Error.WriteLine(
            $"[replay] done — {report.Summary.AmbiguousSlots} slots over {report.Summary.Sentences} sentences, "
            + $"{report.TruthReview.Count} to review → {reportPath}");
        _out.WriteLine($"{report.Summary.AmbiguousSlots} ambiguous slots judged over {report.Summary.Sentences} sentences.");
        _out.WriteLine($"Calibration report → {reportPath}");

        Assert.NotEmpty(report.Slots);
    }

    // Streams the replay's progress to stderr as it runs — a long offline pass is
    // otherwise blind until the single test method returns. One line per judged slot
    // (typed → final, the judge's verdict and margin) plus a heartbeat every 25
    // sentences so liveness shows even across stretches with no ambiguous slot.
    private static void OnReplayProgress(ReplayProgress p)
    {
        foreach (SlotReplayResult s in p.SentenceSlots)
        {
            string verdict = s.JudgeChosen is null
                ? $"abstain({s.AbstainReason})"
                : $"chose '{s.JudgeChosen}' m={s.Margin:0.00}";
            Console.Error.WriteLine($"  [{p.SentenceIndex}] {s.TypedForm}→{s.FinalForm} {verdict}");
        }

        if (p.SentenceIndex % 25 == 0)
            Console.Error.WriteLine($"· {p.SentenceIndex} sentences, {p.TotalSlotsJudged} slots judged");
    }
}
