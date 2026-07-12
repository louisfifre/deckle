using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// End-to-end over in-memory records: the runner aligns each sentence, judges its
// ambiguous slots, calibrates, and — the validity work — counts how records aligned
// (replayed / legacy-repaired / skipped), tallies the closure mix, and lets a
// resolved ground truth override agreement. All against fakes, so it exercises the
// wiring without a corpus file or a model.
[Trait("Category", "unit")]
public sealed class ReplayRunnerTests
{
    private sealed class FakeProbe : IAmbiguityProbe
    {
        // Only « a » folds; every other word is unambiguous and skipped.
        private static readonly AccentVariant[] Aà =
            { new("a", 100), new("à", 50) };

        public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
            word == "a" ? Aà : Array.Empty<AccentVariant>();

        public IReadOnlyList<AccentVariant> SentenceCandidates(string word, bool includeTypedLiteral) =>
            AmbiguousCandidates(word);
    }

    // Always picks the accented form, so agreement depends on the corpus final.
    private sealed class AlwaysÀReranker : ISentenceReranker
    {
        public RerankOutcome Rerank(IReadOnlyList<string> sentence, int slotIndex, IReadOnlyList<AccentVariant> candidates) =>
            new("à", Array.Empty<RerankCandidateScore>(), Margin: 1.5, Threshold: 0.0, AbstainReason: null);
    }

    private static SentenceCorpus.SentenceRecord Record(
        string typed, string final, string history, string closure = "sentence") =>
        new(typed, final, history, closure, string.Empty);

    private static CorpusEntry Modern(string typed, string final, string history, string closure = "sentence") =>
        new(Record(typed, final, history, closure), HistoryPresent: true);

    private static CorpusEntry Legacy(string typed, string final) =>
        new(Record(typed, final, string.Empty), HistoryPresent: false);

    [Fact]
    public void AlignsJudgesAndCalibratesAcrossSentences()
    {
        var corpus = new[]
        {
            // slot 2 « a » ended « à » — the judge agrees.
            Record("je vais a la banque.", "je vais à la banque.", "#2=a»sentence:à"),
            // slot 1 « a » stayed « a » — the judge picks « à », disagrees.
            Record("il a dit.", "il a dit.", ""),
        };

        ReplayReport report = ReplayRunner.Run(corpus, new FakeProbe(), new AlwaysÀReranker());

        Assert.Equal(new ReplaySummary(Sentences: 2, AmbiguousSlots: 2, Chosen: 2, Abstained: 0, AgreedWithFinal: 1), report.Summary);
        Assert.Equal(2, report.Slots.Count);
        Assert.Equal(1, report.Slots.Count(s => s.AgreesWithFinal));
        Assert.Equal(ReplayRunner.DefaultThresholds.Length, report.Calibration.Count);
        Assert.Contains("2 sentences, 2 ambiguous slots", report.Markdown);
    }

    [Fact]
    public void EmptyCorpusYieldsAnEmptyReport()
    {
        ReplayReport report = ReplayRunner.Run(
            Array.Empty<SentenceCorpus.SentenceRecord>(), new FakeProbe(), new AlwaysÀReranker());

        Assert.Equal(0, report.Summary.AmbiguousSlots);
        Assert.Empty(report.Slots);
        Assert.Contains("0 sentences", report.Markdown);
    }

    // The guard against silent degradation: a legacy record repaired via its final
    // string is judged, an unusable record is skipped, and both are counted — never
    // folded into the judged set as final=typed.
    [Fact]
    public void CountsLegacyRepairsAndSkipsInTheIntake()
    {
        var corpus = new[]
        {
            Modern("il a dit.", "il a dit.", ""),            // aligned, judged
            Legacy("mise a jour.", "mise à jour."),          // legacy, repaired from final
            Legacy("cetait bon.", "c'était bon."),           // legacy, token drift → skipped
        };

        ReplayReport report = ReplayRunner.Run(corpus, new FakeProbe(), new AlwaysÀReranker());

        Assert.Equal(2, report.Intake.Replayed);
        Assert.Equal(1, report.Intake.LegacyRepaired);
        Assert.Equal(1, report.Intake.Skipped);
        Assert.Equal(2, report.Summary.Sentences);
        Assert.Contains("2 replayed (1 legacy repaired via final-string), 1 skipped as unusable", report.Markdown);
    }

    [Fact]
    public void ReportsTheClosureMixOfReplayedRecords()
    {
        var corpus = new[]
        {
            Modern("il a dit.", "il a dit.", "", closure: "sentence"),
            Modern("il a dit.", "il a dit.", "", closure: "enter"),
            Modern("il a dit.", "il a dit.", "", closure: "interrupted"),
        };

        ReplayReport report = ReplayRunner.Run(corpus, new FakeProbe(), new AlwaysÀReranker());

        Assert.Equal(1, report.Intake.ClosedOnSentence);
        Assert.Equal(1, report.Intake.ClosedOnEnter);
        Assert.Equal(1, report.Intake.Interrupted);
        Assert.Contains("1 sentence, 1 enter, 1 interrupted", report.Markdown);
    }

    // A slot the judge overruled the final on lands in the review sheet; a resolved
    // truth for its key then makes agreement measure against the truth, not the final.
    [Fact]
    public void ResolvedTruthOverridesAgreement()
    {
        var corpus = new[] { Modern("il a dit.", "il a dit.", "") };

        // Without a truth, the judge's « à » disagrees with the final « a ».
        ReplayReport plain = ReplayRunner.Run(corpus, new FakeProbe(), new AlwaysÀReranker());
        Assert.Equal(0, plain.Summary.AgreedWithFinal);
        TruthReviewRow row = Assert.Single(plain.TruthReview);
        Assert.Equal("a", row.TypedForm);
        Assert.Equal("a", row.FinalForm);
        Assert.Equal("à", row.JudgePick);

        // The maintainer resolves that slot's truth to « à » — the judge was right.
        var truths = new Dictionary<string, string> { [row.Key] = "à" };
        ReplayReport overlaid = ReplayRunner.Run(corpus, new FakeProbe(), new AlwaysÀReranker(), resolvedTruths: truths);

        Assert.Equal(1, overlaid.Summary.AgreedWithFinal);
        Assert.Equal(1, overlaid.Intake.TruthOverlaid);
    }
}
