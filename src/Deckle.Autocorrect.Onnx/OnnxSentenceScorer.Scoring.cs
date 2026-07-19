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
    public SentenceScoringOutcome Score(IReadOnlyList<string> candidates)
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
            CandidateScore[] forwardScores = ScoreCandidatesInOrder(candidates);
            CandidateScore[] combinedScores = forwardScores;
            if (candidates.Count > 1)
            {
                string[] reversedCandidates = candidates.Reverse().ToArray();
                CandidateScore[] reversedScores = ScoreCandidatesInOrder(reversedCandidates);
                combinedScores = new CandidateScore[candidates.Count];

                for (int i = 0; i < candidates.Count; i++)
                    combinedScores[i] = CandidateScore.Average(
                        forwardScores[i],
                        reversedScores[candidates.Count - 1 - i]);
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
        return new SentenceScoringOutcome(
            cleared ? scores[best].Text : null,
            scores,
            margin,
            _margin,
            cleared ? null : SentenceScoringOutcome.AbstainReasons.BelowMargin);
    }

    private CandidateScore[] ScoreCandidatesInOrder(IReadOnlyList<string> candidates)
    {
        int[] promptTokens = AddBosIfNeeded(Encode(BuildScoringPrompt(candidates)));
        if (promptTokens.Length == 0)
            return CandidateScore.AbstainedMany(candidates.Count, SentenceScoringOutcome.AbstainReasons.TooFewTokens);
        if (candidates.Any(static c => string.IsNullOrWhiteSpace(c)))
            return CandidateScore.AbstainedMany(candidates.Count, SentenceScoringOutcome.AbstainReasons.EmptyCandidate);

        int[][] completionTokens = candidates
            .Select(candidate => StripBos(Encode(candidate + "\n")))
            .ToArray();
        CandidateCompletionPlan[] plans = CandidateCompletionPlan.Create(completionTokens);

        var scores = new CandidateScore[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
            scores[i] = ScoreCompletion(promptTokens, completionTokens[i], plans[i]);

        return scores;
    }

    private CandidateScore ScoreCompletion(
        int[] promptTokens,
        int[] completionTokens,
        CandidateCompletionPlan plan)
    {
        if (completionTokens.Length == 0)
            return CandidateScore.Abstained(SentenceScoringOutcome.AbstainReasons.TooFewTokens);
        if (plan.Count <= 0 ||
            plan.Start < 0 ||
            plan.EndExclusive > completionTokens.Length)
            return CandidateScore.Abstained(SentenceScoringOutcome.AbstainReasons.TooFewTokens);

        // One forward over prompt + the scored completion span, reading the
        // teacher-forced logits at every scored position from a single pass.
        // DirectML rejects continuous decoding (a second AppendTokens on a live
        // generator), so the earlier incremental per-token loop cannot run there;
        // feeding the whole span at once is also one forward instead of N, and
        // causal masking makes each scored row identical to the incremental read.
        int promptLen = promptTokens.Length;
        var input = new int[promptLen + plan.EndExclusive];
        Array.Copy(promptTokens, 0, input, 0, promptLen);
        Array.Copy(completionTokens, 0, input, promptLen, plan.EndExclusive);

        using var generatorParams = new GeneratorParams(_model);
        generatorParams.SetSearchOption("max_length", input.Length + 1);

        using var generator = new Generator(_model, generatorParams);
        generator.AppendTokens(input);

        using Tensor logits = generator.GetOutput(LogitsOutputName);
        long numElements = logits.NumElements();
        if (numElements % _vocabSize != 0)
            return CandidateScore.Abstained(SentenceScoringOutcome.AbstainReasons.LogitsUnavailable);
        int rows = (int)(numElements / _vocabSize);

        double logProbability = 0.0;
        int scored = 0;
        for (int next = plan.Start; next < plan.EndExclusive; next++)
        {
            int tokenId = completionTokens[next];
            if (tokenId < 0 || tokenId >= _vocabSize)
                return CandidateScore.Abstained(SentenceScoringOutcome.AbstainReasons.TokenOutOfVocab);

            // The logits at position p score the token at position p + 1, so the
            // distribution for completion token `next` is read at promptLen+next-1.
            int predictPos = promptLen + next - 1;
            if (predictPos < 0 || predictPos >= rows)
                return CandidateScore.Abstained(SentenceScoringOutcome.AbstainReasons.LogitsUnavailable);

            float[] row = LogitsRow(logits, predictPos);
            if (row.Length <= tokenId)
                return CandidateScore.Abstained(SentenceScoringOutcome.AbstainReasons.LogitsUnavailable);

            logProbability += LogProbability(row, tokenId);
            scored++;
        }

        if (scored == 0)
            return CandidateScore.Abstained(SentenceScoringOutcome.AbstainReasons.TooFewTokens);

        return new CandidateScore(
            Score: logProbability / scored,
            LogProbability: logProbability,
            ScoredTokenCount: scored,
            AbstainReason: null);
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
