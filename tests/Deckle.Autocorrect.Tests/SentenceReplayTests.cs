using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The offline replay core: it walks a sentence's slots, judges the ambiguous ones
// with the final sentence as context, and tallies agreement — all against scripted
// fakes, no lexicon and no model.
[Trait("Category", "unit")]
public sealed class SentenceReplayTests
{
    private sealed class FakeProbe : IAmbiguityProbe
    {
        private readonly Dictionary<string, AccentVariant[]> _map;
        public FakeProbe(Dictionary<string, AccentVariant[]> map) => _map = map;
        public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) => Get(word);
        public IReadOnlyList<AccentVariant> SentenceCandidates(string word, bool includeTypedLiteral) => Get(word);
        private AccentVariant[] Get(string word) => _map.TryGetValue(word, out AccentVariant[]? v) ? v : Array.Empty<AccentVariant>();
    }

    private sealed class RecordingReranker : ISentenceReranker
    {
        public readonly List<(IReadOnlyList<string> Sentence, int Slot, IReadOnlyList<AccentVariant> Candidates)> Calls = new();
        private readonly Func<int, RerankOutcome> _verdict;
        public RecordingReranker(Func<int, RerankOutcome> verdict) => _verdict = verdict;
        public RerankOutcome Rerank(IReadOnlyList<string> sentence, int slotIndex, IReadOnlyList<AccentVariant> candidates)
        {
            Calls.Add((sentence, slotIndex, candidates));
            return _verdict(slotIndex);
        }
    }

    [Fact]
    public void JudgesAmbiguousSlotsWithTheFinalSentenceAsContextAndScoresAgreement()
    {
        var typed = new[] { "je", "vais", "a", "la", "banque" };
        var final = new[] { "je", "vais", "à", "la", "banque" };
        var probe = new FakeProbe(new()
        {
            ["a"] = new[] { new AccentVariant("a", 100), new AccentVariant("à", 500) },
        });
        var reranker = new RecordingReranker(_ =>
            new RerankOutcome("à", Array.Empty<RerankCandidateScore>(), 3.0, 0.25, null));

        var results = SentenceReplay.ReplaySentence(typed, final, probe, reranker);

        SlotReplayResult slot = Assert.Single(results);
        Assert.Equal(2, slot.SlotIndex);
        Assert.Equal("a", slot.TypedForm);
        Assert.Equal("à", slot.FinalForm);
        Assert.Equal("à", slot.JudgeChosen);
        Assert.True(slot.AgreesWithFinal);
        Assert.False(slot.Abstained);

        var call = Assert.Single(reranker.Calls);
        Assert.Equal(final, call.Sentence); // the FINAL sentence is the judge's context
        Assert.Equal(2, call.Slot);
        Assert.Equal(new[] { "a", "à" }, call.Candidates.Select(c => c.Form));

        Assert.Equal(new ReplaySummary(1, 1, 1, 0, 1), SentenceReplay.Summarize(results, sentenceCount: 1));
    }

    [Fact]
    public void CountsAnAbstentionAsNeitherChosenNorAgreed()
    {
        var typed = new[] { "il", "a", "dit" };
        var final = new[] { "il", "a", "dit" };
        var probe = new FakeProbe(new()
        {
            ["a"] = new[] { new AccentVariant("a", 100), new AccentVariant("à", 50) },
        });
        var reranker = new RecordingReranker(_ =>
            RerankOutcome.Abstained(RerankOutcome.AbstainReasons.BelowMargin));

        var results = SentenceReplay.ReplaySentence(typed, final, probe, reranker);

        SlotReplayResult slot = Assert.Single(results);
        Assert.True(slot.Abstained);
        Assert.False(slot.AgreesWithFinal);
        Assert.Equal(RerankOutcome.AbstainReasons.BelowMargin, slot.AbstainReason);

        Assert.Equal(new ReplaySummary(1, 1, 0, 1, 0), SentenceReplay.Summarize(results, sentenceCount: 1));
    }

    [Fact]
    public void SkipsSlotsWithFewerThanTwoCandidates()
    {
        var typed = new[] { "bonjour", "le", "monde" };
        var final = new[] { "bonjour", "le", "monde" };
        var probe = new FakeProbe(new()); // nothing ambiguous
        var reranker = new RecordingReranker(_ =>
            throw new InvalidOperationException("a non-ambiguous slot must not be judged"));

        var results = SentenceReplay.ReplaySentence(typed, final, probe, reranker);

        Assert.Empty(results);
        Assert.Empty(reranker.Calls);
    }

    [Fact]
    public void RightContextReplayShowsOnlyTheRequestedFollowingWords()
    {
        var typed = new[] { "on", "est", "sur", "mais", "oui" };
        var final = new[] { "on", "est", "sûr", "mais", "oui" };
        var probe = new FakeProbe(new()
        {
            ["sur"] = new[] { new AccentVariant("sur", 100), new AccentVariant("sûr", 50) },
        });
        var reranker = new RecordingReranker(_ =>
            new RerankOutcome("sûr", Array.Empty<RerankCandidateScore>(), 2.0, 1.0, null));

        var results = SentenceReplay.ReplaySentence(
            typed, final, probe, reranker, rightContextWords: 1);

        Assert.Single(results);
        Assert.Equal(new[] { "on", "est", "sûr", "mais" },
            Assert.Single(reranker.Calls).Sentence);
    }
}
