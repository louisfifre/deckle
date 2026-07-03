using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Onnx;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The adapter bridges the closed-sentence judge (ISentenceScorer) to the slot
// reranker the sentence stage speaks (ISentenceReranker). These exercise the pure
// bridging logic against a scripted scorer — no ONNX runtime, no model.
[Trait("Category", "unit")]
public sealed class OnnxSlotRerankerTests
{
    private sealed class FakeScorer : ISentenceScorer
    {
        private readonly Func<IReadOnlyList<string>, SentenceScoringOutcome> _fn;
        public IReadOnlyList<string>? Received;
        public FakeScorer(Func<IReadOnlyList<string>, SentenceScoringOutcome> fn) => _fn = fn;
        public SentenceScoringOutcome Score(IReadOnlyList<string> candidates)
        {
            Received = candidates;
            return _fn(candidates);
        }
    }

    [Fact]
    public void ScoresTheSlotVariantsAsFullSentencesAndPicksTheJudgesWinner()
    {
        var sentence = new[] { "je", "vais", "a", "la", "banque" };
        var candidates = new[] { new AccentVariant("a", 100), new AccentVariant("à", 500) };

        var scorer = new FakeScorer(cands => new SentenceScoringOutcome(
            Chosen: cands[1], // the judge prefers the "à" sentence
            Scores: new[]
            {
                new SentenceCandidateScore(cands[0], -5.0, -50.0, 10),
                new SentenceCandidateScore(cands[1], -2.0, -20.0, 10),
            },
            Margin: 3.0,
            Threshold: 0.25,
            AbstainReason: null));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);

        RerankOutcome outcome = reranker.Rerank(sentence, slotIndex: 2, candidates);

        // The slot word is swapped, the rest of the sentence held fixed, order kept.
        Assert.Equal(new[] { "je vais a la banque", "je vais à la banque" }, scorer.Received);
        // The winning full sentence maps back to the "à" surface form.
        Assert.Equal("à", outcome.Chosen);
        Assert.Null(outcome.AbstainReason);
        Assert.Equal(3.0, outcome.Margin);
        Assert.Equal(0.25, outcome.Threshold);
        // Per-sentence scores remap to per-form scores, in candidate order.
        Assert.Equal(new[] { "a", "à" }, outcome.Scores.Select(s => s.Form));
        Assert.Equal(-2.0, outcome.Scores[1].Score);
    }

    [Fact]
    public void SurfacesTheJudgesAbstainReasonWhenItDeclines()
    {
        var sentence = new[] { "il", "a", "dit" };
        var candidates = new[] { new AccentVariant("a", 100), new AccentVariant("à", 50) };
        var scorer = new FakeScorer(_ =>
            SentenceScoringOutcome.Abstained(SentenceScoringOutcome.AbstainReasons.BelowMargin));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);

        RerankOutcome outcome = reranker.Rerank(sentence, slotIndex: 1, candidates);

        Assert.Null(outcome.Chosen);
        Assert.Equal(SentenceScoringOutcome.AbstainReasons.BelowMargin, outcome.AbstainReason);
    }

    [Fact]
    public void AbstainsWithoutCallingTheJudgeOnAnEmptySetOrOutOfRangeSlot()
    {
        var scorer = new FakeScorer(_ => throw new InvalidOperationException("the judge must not be consulted"));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);
        var pair = new[] { new AccentVariant("a", 1), new AccentVariant("à", 1) };

        Assert.Equal(
            RerankOutcome.AbstainReasons.NoRule,
            reranker.Rerank(new[] { "x" }, 0, Array.Empty<AccentVariant>()).AbstainReason);
        Assert.Equal(
            RerankOutcome.AbstainReasons.Error,
            reranker.Rerank(new[] { "x" }, slotIndex: 5, pair).AbstainReason);
        Assert.Null(scorer.Received);
    }
}
