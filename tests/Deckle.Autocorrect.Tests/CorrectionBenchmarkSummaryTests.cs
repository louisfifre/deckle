using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class CorrectionBenchmarkSummaryTests
{
    [Fact]
    public void CreateCountsCorrectFixAsPreciseChange()
    {
        CorrectionBenchmarkSummary summary = CorrectionBenchmarkSummary.Create(new[]
        {
            Result(Case(literalIndex: 0, goldIndex: 1), bestIndex: 1, margin: 0.5),
        }, threshold: 0.25);

        Assert.Equal(1, summary.Changes);
        Assert.Equal(1, summary.Fixes);
        Assert.Equal(0, summary.WrongChanges);
        Assert.Equal(1.0, summary.ChangePrecision);
        Assert.Equal(1.0, summary.CorrectionRecall);
    }

    [Fact]
    public void CreateCountsNonLiteralWrongChoiceAsWrongChange()
    {
        CorrectionBenchmarkSummary summary = CorrectionBenchmarkSummary.Create(new[]
        {
            Result(Case(literalIndex: 0, goldIndex: 0), bestIndex: 1, margin: 0.5),
        }, threshold: 0.25);

        Assert.Equal(1, summary.Changes);
        Assert.Equal(0, summary.Fixes);
        Assert.Equal(1, summary.WrongChanges);
        Assert.Equal(0.0, summary.ChangePrecision);
        Assert.Equal(1, summary.CorrectKeeps + summary.SafeAbstentions + summary.WrongChanges);
    }

    [Fact]
    public void CreateCountsBelowThresholdCorrectionAsSafeMiss()
    {
        CorrectionBenchmarkSummary summary = CorrectionBenchmarkSummary.Create(new[]
        {
            Result(Case(literalIndex: 0, goldIndex: 1), bestIndex: 1, margin: 0.1),
        }, threshold: 0.25);

        Assert.Equal(0, summary.Changes);
        Assert.Equal(0, summary.WrongChanges);
        Assert.Equal(1, summary.AbstainedCorrections);
        Assert.Equal(1, summary.Misses);
        Assert.Equal(0.0, summary.CorrectionRecall);
    }

    [Fact]
    public void CreateCountsLiteralChoiceOnCorrectionAsMissedKeep()
    {
        CorrectionBenchmarkSummary summary = CorrectionBenchmarkSummary.Create(new[]
        {
            Result(Case(literalIndex: 0, goldIndex: 1), bestIndex: 0, margin: 0.5),
        }, threshold: 0.25);

        Assert.Equal(0, summary.Changes);
        Assert.Equal(1, summary.MissedKeeps);
        Assert.Equal(1, summary.Misses);
        Assert.Equal(0, summary.WrongChanges);
    }

    [Fact]
    public void CreateTreatsZeroMarginAsAbstention()
    {
        CorrectionBenchmarkSummary summary = CorrectionBenchmarkSummary.Create(new[]
        {
            Result(Case(literalIndex: 0, goldIndex: 1), bestIndex: 1, margin: 0.0),
        }, threshold: 0.0);

        Assert.Equal(0, summary.Changes);
        Assert.Equal(1, summary.AbstainedCorrections);
    }

    [Fact]
    public void FromOutcomeRejectsScoreOrderThatDoesNotMatchCandidates()
    {
        CorrectionBenchmarkCase benchmarkCase = Case(literalIndex: 0, goldIndex: 1);
        var outcome = new SentenceScoringOutcome(
            Chosen: "variant",
            Scores: new[]
            {
                new SentenceCandidateScore("variant", 0.0, 0.0, 1),
                new SentenceCandidateScore("literal", 1.0, 1.0, 1),
            },
            Margin: 1.0,
            Threshold: 0.0,
            AbstainReason: null);

        CorrectionBenchmarkResult result = CorrectionBenchmarkResult.FromOutcome(
            benchmarkCase,
            outcome,
            TimeSpan.Zero);

        Assert.Equal(CorrectionBenchmarkVerdict.ScoringError, result.Verdict(threshold: 0.25));
    }

    [Fact]
    public void ChangePrecisionIsNotMeasuredWhenThereIsNoChange()
    {
        CorrectionBenchmarkSummary summary = CorrectionBenchmarkSummary.Create(new[]
        {
            Result(Case(literalIndex: 0, goldIndex: 1), bestIndex: 1, margin: 0.1),
        }, threshold: 0.25);

        Assert.Equal(0, summary.Changes);
        Assert.True(double.IsNaN(summary.ChangePrecision));
    }

    private static CorrectionBenchmarkCase Case(int literalIndex, int goldIndex) =>
        new(
            "case",
            "category",
            literalIndex,
            goldIndex,
            new[] { "literal", "variant" });

    private static CorrectionBenchmarkResult Result(
        CorrectionBenchmarkCase benchmarkCase,
        int bestIndex,
        double margin) =>
        new(benchmarkCase, bestIndex, margin, TimeSpan.Zero, AbstainReason: null);
}
