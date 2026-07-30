using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class SentenceCanonicalLatencyTests
{
    [Fact]
    public void ArgumentsSelectFrozenCanonicalLatencyDesign()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--sentence-canonical-latency", "--model", "judge", "--provider", "dml"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.SentenceCanonicalLatency, parsed.Mode);
        Assert.Equal("dml", parsed.Provider);
        Assert.Equal("judge", Assert.Single(parsed.Models).Directory);
        Assert.Null(ProbeArguments.Parse(
            ["--sentence-canonical-latency", "--model", "judge", "--iterations", "7"]));
    }

    [Fact]
    public void ScheduleBalancesStrataWithoutImmediateCountDuplicates()
    {
        var calls = Enumerable.Range(0, SentenceCanonicalLatencyCommand.Rounds)
            .SelectMany(round => SentenceCanonicalLatencyCommand.StrataForRound(round)
                .Select((candidateCount, position) => new
                {
                    Round = round,
                    Position = position,
                    CandidateCount = candidateCount,
                }))
            .ToArray();

        Assert.Equal(80, calls.Length);
        foreach (int candidateCount in SentenceProfileFixture.CandidateCounts)
        {
            var stratum = calls.Where(call => call.CandidateCount == candidateCount).ToArray();
            Assert.Equal(20, stratum.Length);
            for (int position = 0; position < 4; position++)
                Assert.Equal(5, stratum.Count(call => call.Position == position));
        }

        Assert.DoesNotContain(
            calls.Zip(calls.Skip(1)),
            pair => pair.First.CandidateCount == pair.Second.CandidateCount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(13)]
    public void CanonicalFixtureKeepsLiteralFirst(int candidateCount)
    {
        ProfileCandidateSet set = SentenceProfileFixture.Candidates(candidateCount, rotation: 0);

        Assert.Equal(Enumerable.Range(0, candidateCount), set.CanonicalIndices);
        Assert.Equal(SentenceProfileFixture.Transaction.Literal, set.Texts[0]);
    }
}
