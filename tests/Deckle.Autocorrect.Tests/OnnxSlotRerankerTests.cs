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
    public void WholeSentenceJudgmentChoosesOneEditAcrossDifferentSlots()
    {
        var transaction = new ClosedSentenceTransaction(
            "Il y a  une seul erreur.",
            new[] { "Il", "y", "a", "une", "seul", "erreur" },
            [
                new SentenceEditCandidate(3, 8, 3, "un"),
                new SentenceEditCandidate(4, 12, 4, "seule"),
            ]);
        var scorer = new FakeScorer(cands => new SentenceScoringOutcome(
            Chosen: cands[2],
            Scores: cands.Select((text, index) =>
                new SentenceCandidateScore(text, -index, -index * 10, 8)).ToArray(),
            Margin: 1.5,
            Threshold: 1.0,
            AbstainReason: null));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);

        RerankOutcome outcome = reranker.RerankSentence(transaction);

        Assert.Equal(
            new[]
            {
                "Il y a  une seul erreur.",
                "Il y a  un seul erreur.",
                "Il y a  une seule erreur.",
            },
            scorer.Received);
        Assert.Equal("seule", outcome.Chosen);
        Assert.Equal(4, outcome.ChosenSlotIndex);
    }

    [Fact]
    public void WholeSentenceLiteralWinnerProducesNoEdit()
    {
        var transaction = new ClosedSentenceTransaction(
            "il y a une seule erreur!",
            new[] { "il", "y", "a", "une", "seule", "erreur" },
            [new SentenceEditCandidate(4, 11, 5, "seul")]);
        var scorer = new FakeScorer(cands => new SentenceScoringOutcome(
            Chosen: cands[0],
            Scores: cands.Select(text =>
                new SentenceCandidateScore(text, -1, -10, 8)).ToArray(),
            Margin: 2.0,
            Threshold: 1.0,
            AbstainReason: null));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);

        RerankOutcome outcome = reranker.RerankSentence(transaction);

        Assert.Null(outcome.Chosen);
        Assert.Null(outcome.ChosenSlotIndex);
        Assert.Null(outcome.AbstainReason);
    }

    public static TheoryData<ClosedSentenceTransaction, string[]> ExactTransactions => new()
    {
        {
            new ClosedSentenceTransaction(
                "la la la reste ici.",
                new[] { "la", "la", "la", "reste", "ici" },
                [new SentenceEditCandidate(1, 3, 2, "là")]),
            new[] { "la la la reste ici.", "la là la reste ici." }
        },
        {
            new ClosedSentenceTransaction(
                "Il\ty a  une seul erreur?!",
                new[] { "Il", "y", "a", "une", "seul", "erreur" },
                [
                    new SentenceEditCandidate(3, 8, 3, "un"),
                    new SentenceEditCandidate(4, 12, 4, "seule"),
                ]),
            new[]
            {
                "Il\ty a  une seul erreur?!",
                "Il\ty a  un seul erreur?!",
                "Il\ty a  une seule erreur?!",
            }
        },
        {
            new ClosedSentenceTransaction(
                "Un cafe\u0301 ☕ reste tres bon.",
                new[] { "Un", "cafe\u0301", "reste", "tres", "bon" },
                [new SentenceEditCandidate(1, 3, 5, "café")]),
            new[] { "Un cafe\u0301 ☕ reste tres bon.", "Un café ☕ reste tres bon." }
        },
    };

    [Theory]
    [MemberData(nameof(ExactTransactions))]
    public void WholeSentenceVariantsPreserveEveryUnitOutsideTheDeclaredEdit(
        ClosedSentenceTransaction transaction,
        string[] expected)
    {
        var scorer = new FakeScorer(candidates => new SentenceScoringOutcome(
            Chosen: candidates[0],
            Scores: candidates.Select(text =>
                new SentenceCandidateScore(text, -1, -10, 8)).ToArray(),
            Margin: 2.0,
            Threshold: 1.0,
            AbstainReason: null));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);

        RerankOutcome outcome = reranker.RerankSentence(transaction);

        Assert.Equal(expected, scorer.Received);
        Assert.Null(outcome.Chosen);
        Assert.Null(outcome.AbstainReason);
    }

    public static TheoryData<SentenceEditCandidate> MalformedEdits => new()
    {
        new SentenceEditCandidate(-1, 11, 4, "seule"),
        new SentenceEditCandidate(9, 11, 4, "seule"),
        new SentenceEditCandidate(4, -1, 4, "seule"),
        new SentenceEditCandidate(4, 11, -1, "seule"),
        new SentenceEditCandidate(4, 99, 4, "seule"),
        new SentenceEditCandidate(4, 11, 5, "seule"),
        new SentenceEditCandidate(4, 11, 4, " "),
    };

    [Theory]
    [MemberData(nameof(MalformedEdits))]
    public void WholeSentenceJudgmentRejectsMalformedPositionalEdits(
        SentenceEditCandidate edit)
    {
        var scorer = new FakeScorer(_ =>
            throw new InvalidOperationException("Malformed edits must not reach the judge."));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);
        var transaction = new ClosedSentenceTransaction(
            "Il y a une seul erreur.",
            new[] { "Il", "y", "a", "une", "seul", "erreur" },
            [edit]);

        RerankOutcome outcome = reranker.RerankSentence(transaction);

        Assert.Equal(RerankOutcome.AbstainReasons.Error, outcome.AbstainReason);
        Assert.Null(scorer.Received);
    }

    [Fact]
    public void SurfacesTheJudgesAbstainReasonWhenItDeclines()
    {
        // Four word tokens clears the context floor so the judge is actually
        // consulted; the point here is that its abstention is surfaced verbatim.
        var sentence = new[] { "il", "nous", "a", "dit" };
        var candidates = new[] { new AccentVariant("a", 100), new AccentVariant("à", 50) };
        var scorer = new FakeScorer(_ =>
            SentenceScoringOutcome.Abstained(SentenceScoringOutcome.AbstainReasons.BelowMargin));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);

        RerankOutcome outcome = reranker.Rerank(sentence, slotIndex: 2, candidates);

        Assert.Null(outcome.Chosen);
        Assert.Equal(SentenceScoringOutcome.AbstainReasons.BelowMargin, outcome.AbstainReason);
    }

    [Fact]
    public void AbstainsOnAShortSentenceWithoutConsultingTheJudge()
    {
        // Three word tokens is below the context floor: the judge is never asked,
        // and the pinned short-context reason is surfaced. The confusion this
        // guards is a sentence-initial imperative read as a participle.
        var scorer = new FakeScorer(_ =>
            throw new InvalidOperationException("the judge must not be consulted below the context floor"));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);
        var sentence = new[] { "continue", "le", "travail" };
        var candidates = new[] { new AccentVariant("continue", 100), new AccentVariant("continué", 5) };

        RerankOutcome outcome = reranker.Rerank(sentence, slotIndex: 0, candidates);

        Assert.Null(outcome.Chosen);
        Assert.Equal(RerankOutcome.AbstainReasons.ShortContext, outcome.AbstainReason);
        Assert.Null(scorer.Received);
    }

    [Fact]
    public void JudgesASentenceThatMeetsTheContextFloor()
    {
        // Four word tokens is exactly the floor: the same slot now reaches the judge.
        var sentence = new[] { "il", "continue", "le", "travail" };
        var candidates = new[] { new AccentVariant("continue", 100), new AccentVariant("continué", 5) };
        var scorer = new FakeScorer(cands => new SentenceScoringOutcome(
            Chosen: cands[0],
            Scores: new[]
            {
                new SentenceCandidateScore(cands[0], -1.0, -10.0, 8),
                new SentenceCandidateScore(cands[1], -4.0, -40.0, 8),
            },
            Margin: 3.0,
            Threshold: 0.25,
            AbstainReason: null));
        using var reranker = new OnnxSlotReranker(scorer, ownsScorer: false);

        RerankOutcome outcome = reranker.Rerank(sentence, slotIndex: 1, candidates);

        Assert.NotNull(scorer.Received); // the floor let it through to the judge
        Assert.Equal("continue", outcome.Chosen);
        Assert.NotEqual(RerankOutcome.AbstainReasons.ShortContext, outcome.AbstainReason);
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
