using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class SentenceProfileTests
{
    [Fact]
    public void ArgumentsSelectSentenceProfileWithOneModelAndRounds()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--sentence-profile", "--model", "judge", "--provider", "dml", "--iterations", "7"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.SentenceProfile, parsed.Mode);
        Assert.Equal(7, parsed.Iterations);
        Assert.Equal("dml", parsed.Provider);
        Assert.Equal("judge", Assert.Single(parsed.Models).Directory);
    }

    [Fact]
    public void FourRoundScheduleBalancesEveryStratumPosition()
    {
        IReadOnlyList<IReadOnlyList<int>> rounds = Enumerable.Range(0, 4)
            .Select(SentenceProfileFixture.StrataForRound)
            .ToArray();

        foreach (int candidateCount in SentenceProfileFixture.CandidateCounts)
            for (int position = 0; position < 4; position++)
                Assert.Single(rounds, round => round[position] == candidateCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(13)]
    public void CandidateRotationCoversEveryOffsetIndependently(int candidateCount)
    {
        int[] rotations = Enumerable.Range(0, candidateCount)
            .Select(round => SentenceProfileFixture.CandidateRotation(round, candidateCount))
            .ToArray();

        Assert.Equal(Enumerable.Range(0, candidateCount).Order(), rotations.Order());
    }

    [Fact]
    public void OutcomeEquivalenceRequiresEverySemanticFieldToMatchExactly()
    {
        var baseline = new SentenceScoringOutcome(
            "correct",
            [new SentenceCandidateScore("correct", 1.0, -2.0, 3)],
            0.5,
            0.25,
            null);
        var identical = new SentenceScoringOutcome(
            "correct",
            [new SentenceCandidateScore("correct", 1.0, -2.0, 3)],
            0.5,
            0.25,
            null);

        Assert.True(SentenceProfileCommand.OutcomesAreExactlyEquivalent(baseline, identical));
        Assert.False(SentenceProfileCommand.OutcomesAreExactlyEquivalent(
            baseline,
            identical with { Margin = double.BitIncrement(identical.Margin) }));
        Assert.False(SentenceProfileCommand.OutcomesAreExactlyEquivalent(
            baseline,
            identical with
            {
                Scores =
                [
                    identical.Scores[0] with
                    {
                        LogProbability = double.BitIncrement(
                            identical.Scores[0].LogProbability),
                    },
                ],
            }));
        Assert.False(SentenceProfileCommand.OutcomesAreExactlyEquivalent(
            baseline,
            identical with
            {
                Scores = [identical.Scores[0] with { ScoredTokenCount = 4 }],
            }));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(13)]
    public void FixtureCandidatesAreExactlyOneDeclaredPositionalEdit(int candidateCount)
    {
        ClosedSentenceTransaction transaction = SentenceProfileFixture.Transaction;
        ProfileCandidateSet set = SentenceProfileFixture.Candidates(candidateCount, rotation: 3);

        Assert.Equal(candidateCount, set.Texts.Count);
        Assert.Equal(Enumerable.Range(0, candidateCount).Order(), set.CanonicalIndices.Order());
        for (int presented = 0; presented < set.Texts.Count; presented++)
        {
            int canonical = set.CanonicalIndices[presented];
            if (canonical == 0)
            {
                Assert.Equal(transaction.Literal, set.Texts[presented]);
                continue;
            }

            SentenceEditCandidate edit = transaction.Edits[canonical - 1];
            string expected = string.Concat(
                transaction.Literal.AsSpan(0, edit.Start),
                edit.Replacement,
                transaction.Literal.AsSpan(edit.Start + edit.Length));
            Assert.Equal(expected, set.Texts[presented]);
            Assert.Equal(
                transaction.Literal[..edit.Start],
                set.Texts[presented][..edit.Start]);
            Assert.Equal(
                transaction.Literal[(edit.Start + edit.Length)..],
                set.Texts[presented][(edit.Start + edit.Replacement.Length)..]);
        }
    }
}
