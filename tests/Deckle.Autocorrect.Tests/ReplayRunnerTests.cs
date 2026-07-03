using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// End-to-end over in-memory records: the runner aligns each sentence, judges its
// ambiguous slots, and calibrates — all against fakes, so it exercises the wiring
// (reader → alignment → replay → calibration) without a corpus file or a model.
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

    [Fact]
    public void AlignsJudgesAndCalibratesAcrossSentences()
    {
        var corpus = new[]
        {
            // slot 2 « a » ended « à » — the judge agrees.
            new SentenceCorpus.SentenceRecord("je vais a la banque.", "je vais à la banque.", "#2=a»sentence:à"),
            // slot 1 « a » stayed « a » — the judge picks « à », disagrees.
            new SentenceCorpus.SentenceRecord("il a dit.", "il a dit.", ""),
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
}
