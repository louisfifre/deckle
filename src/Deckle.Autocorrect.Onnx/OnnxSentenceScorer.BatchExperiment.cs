using Deckle.Autocorrect;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Autocorrect.Onnx;

public sealed partial class OnnxSentenceScorer
{
    internal SentenceBatchExperimentOutcome ScoreBatchExperimental(
        IReadOnlyList<string> candidates)
    {
        BatchInputPreparation preparation = PrepareBatchInput(candidates);
        if (!preparation.TechnicallyValid)
            return SentenceBatchExperimentOutcome.Failed(
                preparation.FailureStage!,
                preparation.FailureReason!);

        int[] promptTokens = preparation.PromptTokens;
        int[][] completionTokens = preparation.CompletionTokens;
        CandidateCompletionPlan[] plans = preparation.Plans;
        int[][] expectedInputs = preparation.ExpectedInputs;
        int sequenceLength = expectedInputs[0].Length;
        int[] flattenedInputs = FlattenBatchInputs(expectedInputs);

        using var generatorParams = new GeneratorParams(_model);
        generatorParams.SetSearchOption("batch_size", 2);
        generatorParams.SetSearchOption("max_length", sequenceLength + 1);
        using var generator = new Generator(_model, generatorParams);
        generator.AppendTokens(flattenedInputs);
        int[][] observedInputs =
        [
            generator.GetSequence(0).ToArray(),
            generator.GetSequence(1).ToArray(),
        ];
        if (!ExactBatchSequencesMatch(expectedInputs, observedInputs))
        {
            return SentenceBatchExperimentOutcome.Failed(
                "sequence_construction",
                "flat_sequence_mismatch");
        }
        using Tensor logits = generator.GetOutput(LogitsOutputName);

        long[] shape = logits.Shape();
        string? geometryFailure = ValidateBatchTensorGeometry(
            logits.Type(),
            shape,
            logits.NumElements(),
            sequenceLength,
            _vocabSize);
        if (geometryFailure is not null)
            return SentenceBatchExperimentOutcome.Failed("tensor_geometry", geometryFailure);

        var scores = new SentenceCandidateScore[2];
        for (int batchIndex = 0; batchIndex < 2; batchIndex++)
        {
            CandidateCompletionPlan plan = plans[batchIndex];
            double logProbability = 0.0;
            int scored = 0;
            for (int next = plan.Start; next < plan.EndExclusive; next++)
            {
                int tokenId = completionTokens[batchIndex][next];
                if (tokenId < 0 || tokenId >= _vocabSize)
                    return SentenceBatchExperimentOutcome.Failed("scoring", "token_out_of_vocab");

                int predictPosition = promptTokens.Length + next - 1;
                if (predictPosition < 0 || predictPosition >= sequenceLength)
                    return SentenceBatchExperimentOutcome.Failed("scoring", "prediction_position");

                int flatPosition = BatchLogitsPosition(
                    batchIndex,
                    sequenceLength,
                    predictPosition);
                float[] row = LogitsRow(logits, flatPosition);
                if (row.Length != _vocabSize)
                    return SentenceBatchExperimentOutcome.Failed("scoring", "logits_row");

                double tokenLogProbability = LogProbability(row, tokenId);
                if (!double.IsFinite(tokenLogProbability))
                    return SentenceBatchExperimentOutcome.Failed("scoring", "non_finite_score");
                logProbability += tokenLogProbability;
                scored++;
            }

            if (scored == 0)
                return SentenceBatchExperimentOutcome.Failed("scoring", "zero_scored_tokens");
            double score = logProbability / scored;
            if (!double.IsFinite(score) || !double.IsFinite(logProbability))
                return SentenceBatchExperimentOutcome.Failed("scoring", "non_finite_score");
            scores[batchIndex] = new SentenceCandidateScore(
                candidates[batchIndex],
                score,
                logProbability,
                scored);
        }

        SentenceScoringOutcome outcome = DecideExperimental(scores);
        return new SentenceBatchExperimentOutcome(
            outcome,
            promptTokens.Length,
            sequenceLength,
            shape,
            logits.Type().ToString(),
            null,
            null);
    }

    internal SentenceBatchInputGeometry InspectBatchInputExperimental(
        IReadOnlyList<string> candidates)
    {
        BatchInputPreparation preparation = PrepareBatchInput(candidates);
        return new SentenceBatchInputGeometry(
            preparation.TechnicallyValid,
            preparation.PromptTokens.Length,
            preparation.ExpectedInputs.Select(static input => input.Length).ToArray(),
            preparation.Plans.Select(static plan => plan.Count).ToArray(),
            preparation.FailureStage,
            preparation.FailureReason);
    }

    internal SentenceBatchTokenizationInspection InspectBatchTokenizationExperimental(
        IReadOnlyList<string> candidates)
    {
        BatchInputPreparation preparation = PrepareBatchInput(candidates);
        if (!preparation.TechnicallyValid)
            return SentenceBatchTokenizationInspection.Failed(
                preparation.FailureStage!,
                preparation.FailureReason!);

        int[] rawPromptTokens = Encode(preparation.Prompt!);
        using Sequences batch = _tokenizer.EncodeBatch(
            [preparation.Prompt!, preparation.Prompt!]);
        if (batch.NumSequences != 2)
            return SentenceBatchTokenizationInspection.Failed(
                "tokenization",
                "batch_sequence_count");

        int[] first = batch[0].ToArray();
        int[] second = batch[1].ToArray();
        int[] preparedPromptTokens = preparation.PromptTokens;
        int[] normalizedRaw = AddBosIfNeeded(rawPromptTokens);
        int[]? prependedBatch = _bosTokenId is int bosTokenId
            ? PrependToken(first, bosTokenId)
            : null;

        return new SentenceBatchTokenizationInspection(
            true,
            _bosTokenId,
            rawPromptTokens.Length,
            preparedPromptTokens.Length,
            [first.Length, second.Length],
            first.SequenceEqual(second),
            first.SequenceEqual(rawPromptTokens)
                && second.SequenceEqual(rawPromptTokens),
            first.SequenceEqual(preparedPromptTokens)
                && second.SequenceEqual(preparedPromptTokens),
            normalizedRaw.SequenceEqual(preparedPromptTokens),
            prependedBatch is not null
                && prependedBatch.SequenceEqual(preparedPromptTokens),
            FirstMismatch(first, rawPromptTokens),
            FirstMismatch(first, preparedPromptTokens),
            prependedBatch is null
                ? null
                : FirstMismatch(prependedBatch, preparedPromptTokens),
            null,
            null);
    }

    private BatchInputPreparation PrepareBatchInput(
        IReadOnlyList<string> candidates)
    {
        if (candidates.Count != 2)
            return BatchInputPreparation.Failed("input", "candidate_count");
        if (candidates.Any(static candidate => string.IsNullOrWhiteSpace(candidate)))
            return BatchInputPreparation.Failed("input", "empty_candidate");
        if (_vocabSize <= 0)
            return BatchInputPreparation.Failed("input", "vocab_size_missing");

        string prompt = BuildScoringPrompt(candidates);
        int[] promptTokens = AddBosIfNeeded(Encode(prompt));
        if (promptTokens.Length == 0)
            return BatchInputPreparation.Failed("tokenization", "empty_prompt");

        int[][] completionTokens = candidates
            .Select(candidate => StripBos(Encode(candidate + "\n")))
            .ToArray();
        CandidateCompletionPlan[] plans = CandidateCompletionPlan.Create(completionTokens);
        if (plans.Length != 2)
            return BatchInputPreparation.Failed("planning", "plan_count");

        int[][] expectedInputs = new int[2][];
        for (int batchIndex = 0; batchIndex < 2; batchIndex++)
        {
            CandidateCompletionPlan plan = plans[batchIndex];
            if (completionTokens[batchIndex].Length == 0
                || plan.Count <= 0
                || plan.Start < 0
                || plan.EndExclusive > completionTokens[batchIndex].Length)
            {
                return BatchInputPreparation.Failed(
                    "planning",
                    "invalid_completion_plan");
            }

            expectedInputs[batchIndex] = ComposeBatchInput(
                promptTokens,
                completionTokens[batchIndex],
                plan.EndExclusive);
        }

        if (expectedInputs[0].Length != expectedInputs[1].Length)
        {
            return new BatchInputPreparation(
                prompt,
                promptTokens,
                completionTokens,
                plans,
                expectedInputs,
                "input_geometry",
                "unequal_sequence_lengths");
        }

        return new BatchInputPreparation(
            prompt,
            promptTokens,
            completionTokens,
            plans,
            expectedInputs,
            null,
            null);
    }

    internal static int[] ComposeBatchInput(
        IReadOnlyList<int> promptTokens,
        IReadOnlyList<int> completionTokens,
        int completionEndExclusive)
    {
        if (completionEndExclusive < 0 || completionEndExclusive > completionTokens.Count)
            throw new ArgumentOutOfRangeException(nameof(completionEndExclusive));

        var input = new int[promptTokens.Count + completionEndExclusive];
        for (int index = 0; index < promptTokens.Count; index++)
            input[index] = promptTokens[index];
        for (int index = 0; index < completionEndExclusive; index++)
            input[promptTokens.Count + index] = completionTokens[index];
        return input;
    }

    internal static int[] PrependToken(IReadOnlyList<int> tokens, int token)
    {
        var result = new int[tokens.Count + 1];
        result[0] = token;
        for (int index = 0; index < tokens.Count; index++)
            result[index + 1] = tokens[index];
        return result;
    }

    internal static int[] FlattenBatchInputs(IReadOnlyList<int[]> inputs)
    {
        if (inputs.Count != 2)
            throw new ArgumentException("Exactly two batch inputs are required.", nameof(inputs));
        int sequenceLength = inputs[0].Length;
        if (sequenceLength <= 0 || inputs[1].Length != sequenceLength)
            throw new ArgumentException("Batch inputs must have equal nonzero lengths.", nameof(inputs));

        var flattened = new int[checked(2 * sequenceLength)];
        Array.Copy(inputs[0], 0, flattened, 0, sequenceLength);
        Array.Copy(inputs[1], 0, flattened, sequenceLength, sequenceLength);
        return flattened;
    }

    internal static bool ExactBatchSequencesMatch(
        IReadOnlyList<int[]> expected,
        IReadOnlyList<int[]> observed)
    {
        if (expected.Count != 2 || observed.Count != 2)
            return false;
        return expected[0].AsSpan().SequenceEqual(observed[0])
            && expected[1].AsSpan().SequenceEqual(observed[1]);
    }

    internal static int? FirstMismatch(
        IReadOnlyList<int> left,
        IReadOnlyList<int> right)
    {
        int shared = Math.Min(left.Count, right.Count);
        for (int index = 0; index < shared; index++)
        {
            if (left[index] != right[index])
                return index;
        }

        return left.Count == right.Count ? null : shared;
    }

    internal static int BatchLogitsPosition(
        int batchIndex,
        int sequenceLength,
        int predictPosition)
    {
        if (batchIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(batchIndex));
        if (sequenceLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequenceLength));
        if (predictPosition < 0 || predictPosition >= sequenceLength)
            throw new ArgumentOutOfRangeException(nameof(predictPosition));
        return checked((batchIndex * sequenceLength) + predictPosition);
    }

    internal static string? ValidateBatchTensorGeometry(
        ElementType elementType,
        IReadOnlyList<long> shape,
        long numElements,
        int sequenceLength,
        int vocabSize)
    {
        if (elementType is not ElementType.float16 and not ElementType.float32)
            return "tensor_type";
        if (shape.Count != 3)
            return "tensor_rank";
        if (shape[0] != 2)
            return "tensor_batch";
        if (shape[1] != sequenceLength)
            return "tensor_sequence";
        if (shape[2] != vocabSize)
            return "tensor_vocab";

        long expected = checked(2L * sequenceLength * vocabSize);
        return numElements == expected ? null : "tensor_elements";
    }

    private SentenceScoringOutcome DecideExperimental(
        IReadOnlyList<SentenceCandidateScore> scores)
    {
        int best = scores[1].Score > scores[0].Score ? 1 : 0;
        int second = 1 - best;
        double margin = scores[best].Score - scores[second].Score;
        bool cleared = double.IsFinite(margin) && margin > 0.0 && margin >= _margin;
        return new SentenceScoringOutcome(
            cleared ? scores[best].Text : null,
            scores,
            margin,
            _margin,
            cleared ? null : SentenceScoringOutcome.AbstainReasons.BelowMargin);
    }

    private sealed record BatchInputPreparation(
        string? Prompt,
        int[] PromptTokens,
        int[][] CompletionTokens,
        CandidateCompletionPlan[] Plans,
        int[][] ExpectedInputs,
        string? FailureStage,
        string? FailureReason)
    {
        public bool TechnicallyValid => FailureStage is null && FailureReason is null;

        public static BatchInputPreparation Failed(string stage, string reason) => new(
            null,
            Array.Empty<int>(),
            Array.Empty<int[]>(),
            Array.Empty<CandidateCompletionPlan>(),
            Array.Empty<int[]>(),
            stage,
            reason);
    }
}

internal sealed record SentenceBatchExperimentOutcome(
    SentenceScoringOutcome? Outcome,
    int PromptTokens,
    int SequenceLength,
    IReadOnlyList<long> TensorShape,
    string? TensorType,
    string? FailureStage,
    string? FailureReason)
{
    public bool TechnicallyValid => Outcome is not null
        && FailureStage is null
        && FailureReason is null;

    public static SentenceBatchExperimentOutcome Failed(string stage, string reason) =>
        new(null, 0, 0, Array.Empty<long>(), null, stage, reason);
}

internal sealed record SentenceBatchInputGeometry(
    bool TechnicallyValid,
    int PromptTokens,
    IReadOnlyList<int> SequenceLengths,
    IReadOnlyList<int> ScoredTokenCounts,
    string? FailureStage,
    string? FailureReason);

internal sealed record SentenceBatchTokenizationInspection(
    bool TechnicallyValid,
    int? BosTokenId,
    int RawPromptTokenCount,
    int PreparedPromptTokenCount,
    IReadOnlyList<int> BatchPromptTokenCounts,
    bool BatchEntriesIdentical,
    bool BatchMatchesRaw,
    bool BatchMatchesPrepared,
    bool NormalizedRawMatchesPrepared,
    bool PrependedBosBatchMatchesPrepared,
    int? FirstBatchRawMismatch,
    int? FirstBatchPreparedMismatch,
    int? FirstPrependedPreparedMismatch,
    string? FailureStage,
    string? FailureReason)
{
    public static SentenceBatchTokenizationInspection Failed(
        string stage,
        string reason) => new(
            false,
            null,
            0,
            0,
            Array.Empty<int>(),
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            null,
            stage,
            reason);
}
