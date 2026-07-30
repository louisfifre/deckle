using Deckle.Autocorrect.Onnx;
using Deckle.Autocorrect.Probe;
using Microsoft.ML.OnnxRuntimeGenAI;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class SentenceBatchExperimentTests
{
    [Fact]
    public void ParsesTheIsolatedBatchExperimentMode()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--sentence-batch-experiment", "--provider", "dml"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.SentenceBatchExperiment, parsed.Mode);
        Assert.Single(parsed.Models);
        Assert.Equal("dml", parsed.Provider);
    }

    [Fact]
    public void RejectsUnrelatedBatchExperimentOptions()
    {
        Assert.Null(ProbeArguments.Parse(
            ["--sentence-batch-experiment", "--iterations", "2"]));
    }

    [Fact]
    public void WarmupsCounterbalanceMethodAndPresentation()
    {
        Assert.Equal(
            [SentenceBatchExperimentMethod.Sequential, SentenceBatchExperimentMethod.Batch],
            SentenceBatchExperimentFixture.WarmupMethods(0));
        Assert.Equal(
            [SentenceBatchExperimentMethod.Batch, SentenceBatchExperimentMethod.Sequential],
            SentenceBatchExperimentFixture.WarmupMethods(1));
        Assert.False(SentenceBatchExperimentFixture.WarmupUsesReversedPresentation(0));
        Assert.True(SentenceBatchExperimentFixture.WarmupUsesReversedPresentation(1));
    }

    [Fact]
    public void LatencyScheduleBalancesMethodPositionsAsCloselyAsFiveBlocksAllow()
    {
        var sequentialPositions = new int[4];
        var batchPositions = new int[4];

        for (int block = 0;
            block < SentenceBatchExperimentFixture.LatencyBlocks;
            block++)
        {
            IReadOnlyList<SentenceBatchExperimentMethod> methods =
                SentenceBatchExperimentFixture.LatencyMethods(block);
            Assert.Equal(4, methods.Count);
            Assert.Equal(2, methods.Count(
                static method => method == SentenceBatchExperimentMethod.Sequential));
            Assert.Equal(2, methods.Count(
                static method => method == SentenceBatchExperimentMethod.Batch));

            for (int position = 0; position < methods.Count; position++)
            {
                if (methods[position] == SentenceBatchExperimentMethod.Sequential)
                    sequentialPositions[position]++;
                else
                    batchPositions[position]++;
            }
        }

        Assert.Equal([3, 2, 2, 3], sequentialPositions);
        Assert.Equal([2, 3, 3, 2], batchPositions);
    }

    [Fact]
    public void ComposesTheExactTeacherForcedInputWithoutPadding()
    {
        int[] input = OnnxSentenceScorer.ComposeBatchInput(
            [10, 11, 12],
            [20, 21, 22],
            completionEndExclusive: 2);

        Assert.Equal([10, 11, 12, 20, 21], input);
    }

    [Fact]
    public void RejectsACompletionBoundaryOutsideTheExactTokens()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OnnxSentenceScorer.ComposeBatchInput(
                [10],
                [20, 21],
                completionEndExclusive: 3));
    }

    [Theory]
    [InlineData(0, 7, 0, 0)]
    [InlineData(0, 7, 6, 6)]
    [InlineData(1, 7, 0, 7)]
    [InlineData(1, 7, 4, 11)]
    public void MapsBatchSequencePositionsIntoFlatLogitRows(
        int batchIndex,
        int sequenceLength,
        int predictPosition,
        int expected)
    {
        Assert.Equal(
            expected,
            OnnxSentenceScorer.BatchLogitsPosition(
                batchIndex,
                sequenceLength,
                predictPosition));
    }

    [Fact]
    public void AcceptsOnlyTheExactBatchTensorGeometry()
    {
        Assert.Null(OnnxSentenceScorer.ValidateBatchTensorGeometry(
            ElementType.float16,
            [2, 7, 32000],
            numElements: 448000,
            sequenceLength: 7,
            vocabSize: 32000));
        Assert.Null(OnnxSentenceScorer.ValidateBatchTensorGeometry(
            ElementType.float32,
            [2, 7, 32000],
            numElements: 448000,
            sequenceLength: 7,
            vocabSize: 32000));
    }

    [Theory]
    [InlineData(1, 7, 32000, 224000, "tensor_batch")]
    [InlineData(2, 8, 32000, 512000, "tensor_sequence")]
    [InlineData(2, 7, 32001, 448014, "tensor_vocab")]
    [InlineData(2, 7, 32000, 447999, "tensor_elements")]
    public void RejectsUnexpectedBatchTensorGeometry(
        long batch,
        long sequence,
        long vocab,
        long elements,
        string expected)
    {
        Assert.Equal(
            expected,
            OnnxSentenceScorer.ValidateBatchTensorGeometry(
                ElementType.float16,
                [batch, sequence, vocab],
                elements,
                sequenceLength: 7,
                vocabSize: 32000));
    }

    [Fact]
    public void EquivalentOutcomeGateRequiresEverySemanticField()
    {
        SentenceBatchOutcomeSummary baseline = Outcome(
            scores: [-1.0, -2.0],
            logProbabilities: [-3.0, -6.0],
            scoredTokenCounts: [3, 3]);

        Assert.True(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with
            {
                Scores = [-1.0005, -2.0005],
                LogProbabilities = [-3.002, -6.002],
            }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { Scores = [-1.002, -2.0] }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { LogProbabilities = [-3.004, -6.0] }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { ScoredTokenCounts = [2, 3] }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { ChosenPresentedIndex = 1 }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { Margin = 1.01 }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { Threshold = 0.01 }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { AbstainReason = "different" }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { LogProbabilities = [-3.0] }));
    }

    [Fact]
    public void EquivalentOutcomeGateRejectsNonFiniteNumbers()
    {
        SentenceBatchOutcomeSummary baseline = Outcome(
            scores: [-1.0, -2.0],
            logProbabilities: [-3.0, -6.0],
            scoredTokenCounts: [3, 3]);

        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { Margin = double.NaN }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { Scores = [double.NaN, -2.0] }));
        Assert.False(SentenceBatchExperimentCommand.OutcomesEquivalent(
            baseline,
            baseline with { LogProbabilities = [double.PositiveInfinity, -6.0] }));
    }

    [Fact]
    public void LatencyHypothesisRequiresTechnicalAndSemanticValidity()
    {
        Assert.True(SentenceBatchExperimentCommand.LatencyHypothesisPasses(
            technicallyValid: true,
            warmupsEquivalent: true,
            measuredEquivalent: true,
            combinedEquivalent: true,
            medianBlockRatio: 0.75,
            fasterBlocks: 4));
        Assert.False(SentenceBatchExperimentCommand.LatencyHypothesisPasses(
            technicallyValid: false,
            warmupsEquivalent: true,
            measuredEquivalent: true,
            combinedEquivalent: true,
            medianBlockRatio: 0.50,
            fasterBlocks: 5));
        Assert.False(SentenceBatchExperimentCommand.LatencyHypothesisPasses(
            technicallyValid: true,
            warmupsEquivalent: false,
            measuredEquivalent: true,
            combinedEquivalent: true,
            medianBlockRatio: 0.50,
            fasterBlocks: 5));
        Assert.False(SentenceBatchExperimentCommand.LatencyHypothesisPasses(
            technicallyValid: true,
            warmupsEquivalent: true,
            measuredEquivalent: false,
            combinedEquivalent: true,
            medianBlockRatio: 0.50,
            fasterBlocks: 5));
        Assert.False(SentenceBatchExperimentCommand.LatencyHypothesisPasses(
            technicallyValid: true,
            warmupsEquivalent: true,
            measuredEquivalent: true,
            combinedEquivalent: false,
            medianBlockRatio: 0.50,
            fasterBlocks: 5));
    }

    [Fact]
    public void FixtureSelectionChoosesTheFirstEligiblePairWithoutExtraInspection()
    {
        CorrectionBenchmarkCase[] corpus =
        [
            Case("three", ["one", "two", "three"]),
            Case("unequal", ["one", "two"]),
            Case("first-valid", ["one", "two"]),
            Case("later-valid", ["one", "two"]),
        ];
        var inspected = new List<string>();

        SentenceBatchFixtureSelection selected =
            SentenceBatchExperimentCommand.SelectFixture(corpus, candidates =>
            {
                string id = corpus.Single(item =>
                    ReferenceEquals(item.Candidates, candidates)).Id;
                inspected.Add(id);
                return id == "unequal"
                    ? Geometry(
                        technicallyValid: false,
                        failureStage: "input_geometry",
                        failureReason: "unequal_sequence_lengths")
                    : Geometry(technicallyValid: true);
            });

        Assert.Equal(2, selected.CorpusIndex);
        Assert.Equal("first-valid", selected.Case.Id);
        Assert.Equal(["unequal", "first-valid"], inspected);
    }

    [Theory]
    [InlineData("tokenization", "unequal_sequence_lengths")]
    [InlineData("input_geometry", "unexpected_length")]
    [InlineData(null, null)]
    public void FixtureSelectionAbortsOnEveryOtherPreparationFailure(
        string? failureStage,
        string? failureReason)
    {
        CorrectionBenchmarkCase[] corpus =
        [
            Case("invalid", ["one", "two"]),
            Case("would-be-valid", ["one", "two"]),
        ];
        int inspectionCount = 0;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => SentenceBatchExperimentCommand.SelectFixture(corpus, _ =>
            {
                inspectionCount++;
                return Geometry(
                    technicallyValid: false,
                    failureStage,
                    failureReason);
            }));

        Assert.Equal(1, inspectionCount);
        Assert.Contains(failureStage ?? string.Empty, error.Message);
        Assert.Contains(failureReason ?? string.Empty, error.Message);
    }

    private static CorrectionBenchmarkCase Case(
        string id,
        string[] candidates) => new(
            id,
            Category: "synthetic",
            LiteralIndex: 0,
            GoldIndex: candidates.Length - 1,
            candidates);

    private static SentenceBatchInputGeometry Geometry(
        bool technicallyValid,
        string? failureStage = null,
        string? failureReason = null) => new(
            technicallyValid,
            PromptTokens: 10,
            SequenceLengths: [20, 20],
            ScoredTokenCounts: [5, 5],
            failureStage,
            failureReason);

    private static SentenceBatchOutcomeSummary Outcome(
        IReadOnlyList<double> scores,
        IReadOnlyList<double> logProbabilities,
        IReadOnlyList<int> scoredTokenCounts) => new(
            ChosenPresentedIndex: 0,
            Margin: 1.0,
            Threshold: 0.0,
            AbstainReason: null,
            Scores: scores,
            LogProbabilities: logProbabilities,
            ScoredTokenCounts: scoredTokenCounts);
}
