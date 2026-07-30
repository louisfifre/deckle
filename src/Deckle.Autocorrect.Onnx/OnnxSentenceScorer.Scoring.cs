using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deckle.Autocorrect;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Autocorrect.Onnx;

public sealed partial class OnnxSentenceScorer
{
    public SentenceScoringOutcome Score(IReadOnlyList<string> candidates) =>
        ScoreCore(candidates, profile: null, OnnxSentenceScoringOrderMode.Combined);

    internal SentenceScoringOutcome ScoreExperimental(
        IReadOnlyList<string> candidates,
        OnnxSentenceScoringOrderMode orderMode)
    {
        if (!Enum.IsDefined(orderMode))
            throw new ArgumentOutOfRangeException(nameof(orderMode));
        return ScoreCore(candidates, profile: null, orderMode);
    }

    internal ProfiledSentenceScoringOutcome ScoreProfiled(IReadOnlyList<string> candidates)
    {
        var profile = new OnnxSentenceScoringProfileBuilder(candidates.Count);
        long started = Stopwatch.GetTimestamp();
        SentenceScoringOutcome outcome = ScoreCore(
            candidates,
            profile,
            OnnxSentenceScoringOrderMode.Combined);
        long totalTicks = Stopwatch.GetTimestamp() - started;
        return new ProfiledSentenceScoringOutcome(outcome, profile.Build(totalTicks));
    }

    private SentenceScoringOutcome ScoreCore(
        IReadOnlyList<string> candidates,
        OnnxSentenceScoringProfileBuilder? profile,
        OnnxSentenceScoringOrderMode orderMode)
    {
        if (candidates.Count == 0)
            return SentenceScoringOutcome.Abstained(SentenceScoringOutcome.AbstainReasons.NoCandidates);
        if (candidates.Count == 1)
            return SentenceScoringOutcome.Abstained(SentenceScoringOutcome.AbstainReasons.SingleCandidate);
        if (_vocabSize <= 0)
            return SentenceScoringOutcome.Abstained(SentenceScoringOutcome.AbstainReasons.VocabSizeMissing);

        var scores = new SentenceCandidateScore[candidates.Count];
        try
        {
            CandidateScore[] combinedScores;
            if (orderMode == OnnxSentenceScoringOrderMode.ReverseOnly)
            {
                string[] reversedCandidates = candidates.Reverse().ToArray();
                CandidateScore[] reversedScores = ScoreCandidatesInOrder(
                    reversedCandidates,
                    profile: null);
                combinedScores = new CandidateScore[candidates.Count];
                for (int i = 0; i < candidates.Count; i++)
                    combinedScores[i] = reversedScores[candidates.Count - 1 - i];
            }
            else
            {
                OnnxSentenceOrderProfileBuilder? forwardProfile = profile?.BeginOrder(
                    "forward",
                    Enumerable.Range(0, candidates.Count).ToArray());
                CandidateScore[] forwardScores = ScoreCandidatesInOrder(
                    candidates,
                    forwardProfile);
                combinedScores = forwardScores;
                if (orderMode == OnnxSentenceScoringOrderMode.Combined)
                {
                    string[] reversedCandidates = candidates.Reverse().ToArray();
                    OnnxSentenceOrderProfileBuilder? reverseProfile = profile?.BeginOrder(
                        "reverse",
                        Enumerable.Range(0, candidates.Count).Reverse().ToArray());
                    CandidateScore[] reversedScores = ScoreCandidatesInOrder(
                        reversedCandidates,
                        reverseProfile);
                    long combinationStarted = profile is null ? 0 : Stopwatch.GetTimestamp();
                    combinedScores = new CandidateScore[candidates.Count];

                    for (int i = 0; i < candidates.Count; i++)
                        combinedScores[i] = CandidateScore.Average(
                            forwardScores[i],
                            reversedScores[candidates.Count - 1 - i]);
                    if (profile is not null)
                        profile.ScoreCombinationTicks = Stopwatch.GetTimestamp() - combinationStarted;
                }
            }

            for (int i = 0; i < combinedScores.Length; i++)
            {
                CandidateScore score = combinedScores[i];
                if (score.AbstainReason is not null)
                    return new SentenceScoringOutcome(null, scores[..i], 0.0, _margin, score.AbstainReason);

                scores[i] = new SentenceCandidateScore(
                    candidates[i],
                    score.Score,
                    score.LogProbability,
                    score.ScoredTokenCount);
            }
        }
        catch
        {
            return new SentenceScoringOutcome(
                null,
                scores,
                0.0,
                _margin,
                SentenceScoringOutcome.AbstainReasons.Error);
        }

        long decisionStarted = profile is null ? 0 : Stopwatch.GetTimestamp();
        int best = 0;
        int second = -1;
        for (int i = 1; i < scores.Length; i++)
        {
            if (scores[i].Score > scores[best].Score)
            {
                second = best;
                best = i;
            }
            else if (second < 0 || scores[i].Score > scores[second].Score)
            {
                second = i;
            }
        }

        double margin = scores[best].Score - scores[second].Score;
        bool cleared = double.IsFinite(margin) && margin > 0.0 && margin >= _margin;
        var outcome = new SentenceScoringOutcome(
            cleared ? scores[best].Text : null,
            scores,
            margin,
            _margin,
            cleared ? null : SentenceScoringOutcome.AbstainReasons.BelowMargin);
        if (profile is not null)
            profile.FinalDecisionTicks = Stopwatch.GetTimestamp() - decisionStarted;
        return outcome;
    }

    private CandidateScore[] ScoreCandidatesInOrder(
        IReadOnlyList<string> candidates,
        OnnxSentenceOrderProfileBuilder? profile)
    {
        long started = profile is null ? 0 : Stopwatch.GetTimestamp();
        string prompt = BuildScoringPrompt(candidates);
        if (profile is not null)
            profile.PromptRenderTicks = Stopwatch.GetTimestamp() - started;

        started = profile is null ? 0 : Stopwatch.GetTimestamp();
        int[] promptTokens = AddBosIfNeeded(Encode(prompt));
        if (profile is not null)
        {
            profile.PromptTokenizationTicks = Stopwatch.GetTimestamp() - started;
            profile.PromptTokens = promptTokens.Length;
        }
        if (promptTokens.Length == 0)
            return CandidateScore.AbstainedMany(candidates.Count, SentenceScoringOutcome.AbstainReasons.TooFewTokens);
        if (candidates.Any(static c => string.IsNullOrWhiteSpace(c)))
            return CandidateScore.AbstainedMany(candidates.Count, SentenceScoringOutcome.AbstainReasons.EmptyCandidate);

        started = profile is null ? 0 : Stopwatch.GetTimestamp();
        int[][] completionTokens = candidates
            .Select(candidate => StripBos(Encode(candidate + "\n")))
            .ToArray();
        if (profile is not null)
        {
            profile.CompletionTokenizationTicks = Stopwatch.GetTimestamp() - started;
            profile.CompletionTokens = completionTokens
                .Select(static completion => completion.Length)
                .ToArray();
        }

        started = profile is null ? 0 : Stopwatch.GetTimestamp();
        CandidateCompletionPlan[] plans = CandidateCompletionPlan.Create(completionTokens);
        if (profile is not null)
            profile.CompletionPlanTicks = Stopwatch.GetTimestamp() - started;

        var scores = new CandidateScore[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
            scores[i] = ScoreCompletion(
                promptTokens,
                completionTokens[i],
                plans[i],
                profile,
                i);

        return scores;
    }

    private CandidateScore ScoreCompletion(
        int[] promptTokens,
        int[] completionTokens,
        CandidateCompletionPlan plan,
        OnnxSentenceOrderProfileBuilder? profile,
        int presentedIndex)
    {
        CandidateScore result = CandidateScore.Abstained(
            SentenceScoringOutcome.AbstainReasons.TooFewTokens);
        long inputPreparationTicks = 0;
        long generatorSetupTicks = 0;
        long appendTokensTicks = 0;
        long getOutputTicks = 0;
        long logitsReadbackTicks = 0;
        long logSoftmaxTicks = 0;
        long disposalTicks = 0;
        long nativeStarted = 0;
        GeneratorParams? generatorParams = null;
        Generator? generator = null;
        Tensor? logits = null;

        try
        {
            if (completionTokens.Length == 0)
                return result;
            if (plan.Count <= 0 ||
                plan.Start < 0 ||
                plan.EndExclusive > completionTokens.Length)
                return result;

            // One forward over prompt + the scored completion span, reading the
            // teacher-forced logits at every scored position from a single pass.
            // DirectML rejects continuous decoding (a second AppendTokens on a live
            // generator), so the earlier incremental per-token loop cannot run there;
            // feeding the whole span at once is also one forward instead of N, and
            // causal masking makes each scored row identical to the incremental read.
            long started = profile is null ? 0 : Stopwatch.GetTimestamp();
            int promptLen = promptTokens.Length;
            var input = new int[promptLen + plan.EndExclusive];
            Array.Copy(promptTokens, 0, input, 0, promptLen);
            Array.Copy(completionTokens, 0, input, promptLen, plan.EndExclusive);
            if (profile is not null)
                inputPreparationTicks = Stopwatch.GetTimestamp() - started;

            started = profile is null ? 0 : Stopwatch.GetTimestamp();
            generatorParams = new GeneratorParams(_model);
            generatorParams.SetSearchOption("max_length", input.Length + 1);

            generator = new Generator(_model, generatorParams);
            if (profile is not null)
            {
                generatorSetupTicks = Stopwatch.GetTimestamp() - started;
                nativeStarted = Stopwatch.GetTimestamp();
                started = nativeStarted;
            }
            generator.AppendTokens(input);
            if (profile is not null)
                appendTokensTicks = Stopwatch.GetTimestamp() - started;

            started = profile is null ? 0 : Stopwatch.GetTimestamp();
            logits = generator.GetOutput(LogitsOutputName);
            if (profile is not null)
                getOutputTicks = Stopwatch.GetTimestamp() - started;

            long numElements = logits.NumElements();
            if (numElements % _vocabSize != 0)
            {
                result = CandidateScore.Abstained(
                    SentenceScoringOutcome.AbstainReasons.LogitsUnavailable);
                return result;
            }
            int rows = (int)(numElements / _vocabSize);

            double logProbability = 0.0;
            int scored = 0;
            for (int next = plan.Start; next < plan.EndExclusive; next++)
            {
                int tokenId = completionTokens[next];
                if (tokenId < 0 || tokenId >= _vocabSize)
                {
                    result = CandidateScore.Abstained(
                        SentenceScoringOutcome.AbstainReasons.TokenOutOfVocab);
                    return result;
                }

                // The logits at position p score the token at position p + 1, so the
                // distribution for completion token `next` is read at promptLen+next-1.
                int predictPos = promptLen + next - 1;
                if (predictPos < 0 || predictPos >= rows)
                {
                    result = CandidateScore.Abstained(
                        SentenceScoringOutcome.AbstainReasons.LogitsUnavailable);
                    return result;
                }

                started = profile is null ? 0 : Stopwatch.GetTimestamp();
                float[] row = LogitsRow(logits, predictPos);
                if (profile is not null)
                    logitsReadbackTicks += Stopwatch.GetTimestamp() - started;
                if (row.Length <= tokenId)
                {
                    result = CandidateScore.Abstained(
                        SentenceScoringOutcome.AbstainReasons.LogitsUnavailable);
                    return result;
                }

                started = profile is null ? 0 : Stopwatch.GetTimestamp();
                logProbability += LogProbability(row, tokenId);
                if (profile is not null)
                    logSoftmaxTicks += Stopwatch.GetTimestamp() - started;
                scored++;
            }

            if (scored == 0)
                return result;

            result = new CandidateScore(
                Score: logProbability / scored,
                LogProbability: logProbability,
                ScoredTokenCount: scored,
                AbstainReason: null);
            return result;
        }
        finally
        {
            if (profile is not null)
            {
                long disposalStarted = Stopwatch.GetTimestamp();
                DisposeNative(logits, generator, generatorParams);
                disposalTicks = Stopwatch.GetTimestamp() - disposalStarted;
                long nativeEnvelopeTicks = nativeStarted == 0
                    ? 0
                    : Stopwatch.GetTimestamp() - nativeStarted;
                profile.AddCandidate(new OnnxSentenceCandidateProfile(
                    presentedIndex,
                    profile.OriginalIndex(presentedIndex),
                    completionTokens.Length,
                    plan.Count,
                    result.ScoredTokenCount,
                    inputPreparationTicks,
                    generatorSetupTicks,
                    appendTokensTicks,
                    getOutputTicks,
                    logitsReadbackTicks,
                    logSoftmaxTicks,
                    disposalTicks,
                    nativeEnvelopeTicks,
                    result.Score,
                    result.LogProbability,
                    result.AbstainReason));
            }
            else
            {
                DisposeNative(logits, generator, generatorParams);
            }
        }
    }

    private static void DisposeNative(
        Tensor? logits,
        Generator? generator,
        GeneratorParams? generatorParams)
    {
        try
        {
            logits?.Dispose();
        }
        finally
        {
            try
            {
                generator?.Dispose();
            }
            finally
            {
                generatorParams?.Dispose();
            }
        }
    }

    private float[] LogitsRow(Tensor logits, int position)
    {
        int offset = position * _vocabSize;
        switch (logits.Type())
        {
            case ElementType.float32:
            {
                ReadOnlySpan<float> data = logits.GetData<float>();
                if ((long)offset + _vocabSize > data.Length)
                    return Array.Empty<float>();

                return data.Slice(offset, _vocabSize).ToArray();
            }
            case ElementType.float16:
            {
                ReadOnlySpan<Half> data = logits.GetData<Half>();
                if ((long)offset + _vocabSize > data.Length)
                    return Array.Empty<float>();

                ReadOnlySpan<Half> row = data.Slice(offset, _vocabSize);
                var upcast = new float[_vocabSize];
                for (int i = 0; i < _vocabSize; i++)
                    upcast[i] = (float)row[i];

                return upcast;
            }
            default:
                return Array.Empty<float>();
        }
    }

    private static double LogProbability(ReadOnlySpan<float> logits, int tokenId)
    {
        double max = double.NegativeInfinity;
        for (int i = 0; i < logits.Length; i++)
            max = Math.Max(max, logits[i]);

        double sum = 0.0;
        for (int i = 0; i < logits.Length; i++)
            sum += Math.Exp(logits[i] - max);

        return logits[tokenId] - max - Math.Log(sum);
    }

    private readonly record struct CandidateScore(
        double Score,
        double LogProbability,
        int ScoredTokenCount,
        string? AbstainReason)
    {
        public static CandidateScore Abstained(string reason) =>
            new(0.0, 0.0, 0, reason);

        public static CandidateScore[] AbstainedMany(int count, string reason)
        {
            var scores = new CandidateScore[count];
            Array.Fill(scores, Abstained(reason));
            return scores;
        }

        public static CandidateScore Average(CandidateScore left, CandidateScore right)
        {
            if (left.AbstainReason is not null)
                return left;
            if (right.AbstainReason is not null)
                return right;

            return new CandidateScore(
                Score: (left.Score + right.Score) / 2.0,
                LogProbability: (left.LogProbability + right.LogProbability) / 2.0,
                ScoredTokenCount: Math.Max(left.ScoredTokenCount, right.ScoredTokenCount),
                AbstainReason: null);
        }
    }
}

internal enum OnnxSentenceScoringOrderMode
{
    Combined,
    ForwardOnly,
    ReverseOnly,
}
