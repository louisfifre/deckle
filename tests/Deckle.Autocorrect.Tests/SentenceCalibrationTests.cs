using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class SentenceCalibrationTests
{
    [Fact]
    public void ArgumentsSelectFrozenSentenceCalibrationDesign()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--sentence-calibration", "--model", "judge", "--provider", "dml"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.SentenceCalibration, parsed.Mode);
        Assert.Equal("dml", parsed.Provider);
        Assert.Equal("judge", Assert.Single(parsed.Models).Directory);
        Assert.Null(ProbeArguments.Parse(
            ["--sentence-calibration", "--model", "judge", "--iterations", "7"]));
    }

    [Fact]
    public void CrossoverScheduleBalancesMethodAcrossTimeAndCallPosition()
    {
        var rows = Enumerable.Range(
                0,
                SentenceCalibrationFixture.CalibrationBlocksPerStratum)
            .Select(block => new
            {
                Block = block,
                ProfiledOuter = SentenceCalibrationFixture.IsProfiledOuter(block),
            })
            .ToArray();

        Assert.Equal([2], SentenceCalibrationFixture.CalibrationCandidateCounts);
        Assert.Equal(8, rows.Count(static row => row.ProfiledOuter));
        Assert.Equal(4, rows.Take(8).Count(static row => row.ProfiledOuter));
        Assert.Equal(4, rows.Skip(8).Count(static row => row.ProfiledOuter));
        Assert.Equal(4, rows.Where(static row => row.Block % 2 == 0)
            .Count(static row => row.ProfiledOuter));
        Assert.Equal(4, rows.Where(static row => row.Block % 2 != 0)
            .Count(static row => row.ProfiledOuter));

        for (int callPosition = 0; callPosition < 4; callPosition++)
        {
            int profiled = Enumerable.Range(
                    0,
                    SentenceCalibrationFixture.CalibrationBlocksPerStratum)
                .Count(block =>
                    SentenceCalibrationFixture.MethodsForBlock(block)[callPosition]
                    == SentenceCalibrationMethod.Profiled);
            Assert.Equal(8, profiled);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void CrossoverMethodIsBalancedWithinEveryRepeatedRotation(int candidateCount)
    {
        var rows = Enumerable.Range(
                0,
                SentenceCalibrationFixture.CalibrationBlocksPerStratum)
            .Select(block => new
            {
                Rotation = SentenceCalibrationFixture.CalibrationRotation(
                    block,
                    candidateCount),
                ProfiledOuter = SentenceCalibrationFixture.IsProfiledOuter(block),
            })
            .GroupBy(static row => row.Rotation);

        foreach (var rotation in rows)
        {
            Assert.Equal(
                rotation.Count(static row => row.ProfiledOuter),
                rotation.Count(static row => !row.ProfiledOuter));
        }
    }

    [Fact]
    public void OrdinaryScheduleBalancesStrataWithoutImmediateCountDuplicates()
    {
        var calls = Enumerable.Range(0, SentenceCalibrationFixture.OrdinaryRounds)
            .SelectMany(round => SentenceCalibrationFixture
                .OrdinaryStrataForRound(round)
                .Select((candidateCount, position) => new
                {
                    Round = round,
                    Position = position,
                    CandidateCount = candidateCount,
                    Rotation = SentenceCalibrationFixture.OrdinaryRotation(
                        round,
                        candidateCount),
                }))
            .ToArray();

        Assert.Equal(
            Enumerable.Range(0, SentenceCalibrationFixture.OrdinaryRounds),
            SentenceCalibrationFixture.RotationOrdinalsForTests().Order());

        foreach (int candidateCount in SentenceProfileFixture.CandidateCounts)
        {
            var stratum = calls.Where(call => call.CandidateCount == candidateCount).ToArray();
            Assert.Equal(20, stratum.Length);
            for (int position = 0; position < 4; position++)
            {
                var atPosition = stratum.Where(call => call.Position == position).ToArray();
                Assert.Equal(5, atPosition.Length);
                int[] crossTab = Enumerable.Range(0, candidateCount)
                    .Select(rotation => atPosition.Count(call => call.Rotation == rotation))
                    .ToArray();
                if (candidateCount <= 4)
                    Assert.InRange(crossTab.Max() - crossTab.Min(), 0, 1);
                else
                    Assert.Equal(5, atPosition.Select(call => call.Rotation).Distinct().Count());
            }

            int[] marginalRotations = Enumerable.Range(0, candidateCount)
                .Select(rotation => stratum.Count(call => call.Rotation == rotation))
                .ToArray();
            Assert.InRange(marginalRotations.Max() - marginalRotations.Min(), 0, 1);
        }

        Assert.DoesNotContain(
            calls.Zip(calls.Skip(1)),
            pair => pair.First.CandidateCount == pair.Second.CandidateCount);
    }
}
