using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class SentenceProposalVerifierTests
{
    [Fact]
    public void ScoresOriginalBeforeProposalAndAcceptsTheProposedWinner()
    {
        const string original = "Il faut prepare le terrain.";
        const string proposed = "Il faut préparer le terrain.";
        var scorer = new ScriptedScorer(proposed);
        var verifier = new SentenceProposalVerifier(scorer);

        SentenceProposalVerification result = verifier.Verify(original, proposed);

        Assert.Equal(new[] { original, proposed }, scorer.Candidates);
        Assert.Equal(SentenceProposalVerdict.Accept, result.Verdict);
        Assert.Equal(1.25, result.Margin);
        Assert.Equal(1.0, result.Threshold);
        Assert.Equal(2, result.Scores.Count);
    }

    [Fact]
    public void KeepsTheOriginalWhenTheJudgePrefersIt()
    {
        const string original = "Le build passe maintenant.";
        const string proposed = "La construction passe maintenant.";
        var verifier = new SentenceProposalVerifier(new ScriptedScorer(original));

        SentenceProposalVerification result = verifier.Verify(original, proposed);

        Assert.Equal(SentenceProposalVerdict.Keep, result.Verdict);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void AbstentionDoesNotChooseEitherSentence()
    {
        var scorer = new ScriptedScorer(
            chosen: null,
            abstainReason: SentenceScoringOutcome.AbstainReasons.BelowMargin);
        var verifier = new SentenceProposalVerifier(scorer);

        SentenceProposalVerification result = verifier.Verify(
            "Je relis le brief.",
            "Je relis le bref.");

        Assert.Equal(SentenceProposalVerdict.Abstain, result.Verdict);
        Assert.Equal(SentenceScoringOutcome.AbstainReasons.BelowMargin, result.Reason);
    }

    [Fact]
    public void IdentityProposalKeepsWithoutConsultingTheScorer()
    {
        var verifier = new SentenceProposalVerifier(new ThrowingScorer());

        SentenceProposalVerification result = verifier.Verify(
            "Déjà propre.",
            "Déjà propre.");

        Assert.Equal(SentenceProposalVerdict.Keep, result.Verdict);
        Assert.Equal(SentenceProposalVerification.Reasons.Identity, result.Reason);
    }

    [Fact]
    public void AChosenTextOutsideTheClosedSetAbstains()
    {
        var verifier = new SentenceProposalVerifier(new ScriptedScorer("Texte inventé."));

        SentenceProposalVerification result = verifier.Verify(
            "Texte original.",
            "Texte proposé.");

        Assert.Equal(SentenceProposalVerdict.Abstain, result.Verdict);
        Assert.Equal(SentenceScoringOutcome.AbstainReasons.Error, result.Reason);
    }

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(1.5, double.NaN)]
    [InlineData(1.5, -1.0)]
    public void InvalidOrBelowThresholdChosenOutcomeAbstains(double margin, double threshold)
    {
        const string original = "Il faut prepare.";
        const string proposed = "Il faut préparer.";
        var verifier = new SentenceProposalVerifier(
            new OutcomeScorer(original, proposed, proposed, margin, threshold));

        SentenceProposalVerification result = verifier.Verify(original, proposed);

        Assert.Equal(SentenceProposalVerdict.Abstain, result.Verdict);
        Assert.Equal(SentenceScoringOutcome.AbstainReasons.Error, result.Reason);
    }

    [Fact]
    public void ScorerFailureBecomesAnAbstention()
    {
        var verifier = new SentenceProposalVerifier(new ThrowingScorer());

        SentenceProposalVerification result = verifier.Verify(
            "Il faut prepare.",
            "Il faut préparer.");

        Assert.Equal(SentenceProposalVerdict.Abstain, result.Verdict);
        Assert.Equal(SentenceScoringOutcome.AbstainReasons.Error, result.Reason);
    }

    private sealed class ScriptedScorer(
        string? chosen,
        string? abstainReason = null) : ISentenceScorer
    {
        public IReadOnlyList<string> Candidates { get; private set; } = Array.Empty<string>();

        public SentenceScoringOutcome Score(IReadOnlyList<string> candidates)
        {
            Candidates = candidates.ToArray();
            var scores = candidates
                .Select((text, index) => new SentenceCandidateScore(text, index, -index, 4))
                .ToArray();
            return new SentenceScoringOutcome(
                chosen,
                scores,
                Margin: 1.25,
                Threshold: 1.0,
                AbstainReason: abstainReason);
        }
    }

    private sealed class ThrowingScorer : ISentenceScorer
    {
        public SentenceScoringOutcome Score(IReadOnlyList<string> candidates) =>
            throw new InvalidOperationException("Identity must not reach the scorer.");
    }

    private sealed class OutcomeScorer(
        string original,
        string proposed,
        string? chosen,
        double margin,
        double threshold) : ISentenceScorer
    {
        public SentenceScoringOutcome Score(IReadOnlyList<string> candidates) => new(
            chosen,
            [new(original, 1.0, 1.0, 1), new(proposed, 2.0, 2.0, 1)],
            margin,
            threshold,
            null);
    }
}
