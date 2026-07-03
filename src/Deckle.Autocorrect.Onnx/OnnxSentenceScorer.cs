using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deckle.Autocorrect;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Autocorrect.Onnx;

// Minimal ONNX Runtime GenAI scorer for closed sentence candidates. It frames
// the model as a judge, then performs forced decoding over each known candidate
// answer. The applied output remains one of the caller's candidates, never free
// generation.
public sealed class OnnxSentenceScorer : ISentenceScorer, IDisposable
{
    private const string LogitsOutputName = "logits";
    private const string SystemPrompt =
        "You are Deckle's local French autocorrect judge. You choose only among closed candidates.";

    private readonly OgaHandle _ogaHandle;
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly double _margin;
    private readonly int _vocabSize;
    private readonly int? _bosTokenId;
    private readonly string? _chatTemplate;

    public OnnxSentenceScorer(string modelDir, double margin)
    {
        _margin = double.IsFinite(margin) && margin > 0.0 ? margin : 0.0;
        _vocabSize = TryReadVocabSize(modelDir) ?? 0;
        _chatTemplate = TryReadChatTemplate(modelDir);

        OgaHandle? ogaHandle = null;
        Model? model = null;
        Tokenizer? tokenizer = null;
        try
        {
            ogaHandle = new OgaHandle();
            model = new Model(modelDir);
            tokenizer = new Tokenizer(model);

            _ogaHandle = ogaHandle;
            _model = model;
            _tokenizer = tokenizer;
            _bosTokenId = TryGetBosTokenId(_tokenizer);
        }
        catch
        {
            tokenizer?.Dispose();
            model?.Dispose();
            ogaHandle?.Dispose();
            throw;
        }
    }

    public static ISentenceScorer? TryLoad(string modelDir, double margin)
    {
        try
        {
            if (!Directory.Exists(modelDir))
                return null;

            return new OnnxSentenceScorer(modelDir, margin);
        }
        catch
        {
            return null;
        }
    }

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
            .Select(candidate => StripBos(Encode(candidate)))
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

        using var generatorParams = new GeneratorParams(_model);
        generatorParams.SetSearchOption("max_length", promptTokens.Length + plan.EndExclusive + 1);

        using var generator = new Generator(_model, generatorParams);
        generator.AppendTokens(promptTokens);
        if (plan.Start > 0)
            generator.AppendTokens(completionTokens.AsSpan(0, plan.Start));

        double logProbability = 0.0;
        int scored = 0;
        for (int next = plan.Start; next < plan.EndExclusive; next++)
        {
            int tokenId = completionTokens[next];
            if (tokenId < 0 || tokenId >= _vocabSize)
                return CandidateScore.Abstained(SentenceScoringOutcome.AbstainReasons.TokenOutOfVocab);

            using Tensor logits = generator.GetOutput(LogitsOutputName);
            ReadOnlySpan<float> row = LastLogitsRow(logits);
            if (row.Length <= tokenId)
                return CandidateScore.Abstained(SentenceScoringOutcome.AbstainReasons.LogitsUnavailable);

            logProbability += LogProbability(row, tokenId);
            scored++;

            generator.AppendTokens(completionTokens.AsSpan(next, 1));
        }

        return new CandidateScore(
            Score: logProbability / scored,
            LogProbability: logProbability,
            ScoredTokenCount: scored,
            AbstainReason: null);
    }

    private string BuildScoringPrompt(IReadOnlyList<string> candidates)
    {
        string content = BuildJudgeContent(candidates);
        if (_chatTemplate is not null)
        {
            try
            {
                string messages = JsonSerializer.Serialize(new[]
                {
                    new ChatTemplateMessage("system", SystemPrompt),
                    new ChatTemplateMessage("user", content),
                });
                return _tokenizer.ApplyChatTemplate(_chatTemplate, messages, "[]", add_generation_prompt: true);
            }
            catch
            {
                // Some exported tokenizers carry incomplete chat-template support.
            }
        }

        return $"{SystemPrompt}\n\n{content}\n";
    }

    private static string BuildJudgeContent(IReadOnlyList<string> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Task: choose the best corrected French sentence among these closed variants.");
        builder.AppendLine("Rules:");
        builder.AppendLine("- Do not create a new sentence.");
        builder.AppendLine("- Pay attention to accents, homophones, agreement, and verb endings.");
        builder.AppendLine("- Return exactly one candidate text, unchanged.");
        builder.AppendLine();
        builder.AppendLine("Candidates:");

        for (int i = 0; i < candidates.Count; i++)
            builder.Append(i + 1).Append(". ").AppendLine(candidates[i]);

        builder.AppendLine();
        builder.Append("Answer:");
        return builder.ToString();
    }

    private int[] Encode(string text)
    {
        using Sequences sequences = _tokenizer.Encode(text);
        if (sequences.NumSequences != 1)
            return Array.Empty<int>();

        return sequences[0].ToArray();
    }

    private int[] AddBosIfNeeded(int[] tokens)
    {
        if (_bosTokenId is not int bosTokenId)
            return tokens;
        if (tokens.Length > 0 && tokens[0] == bosTokenId)
            return tokens;

        var withBos = new int[tokens.Length + 1];
        withBos[0] = bosTokenId;
        Array.Copy(tokens, 0, withBos, 1, tokens.Length);
        return withBos;
    }

    private int[] StripBos(int[] tokens)
    {
        if (_bosTokenId is not int bosTokenId)
            return tokens;
        if (tokens.Length == 0 || tokens[0] != bosTokenId)
            return tokens;

        return tokens[1..];
    }

    private ReadOnlySpan<float> LastLogitsRow(Tensor logits)
    {
        ReadOnlySpan<float> data = logits.GetData<float>();
        if (data.Length < _vocabSize || data.Length % _vocabSize != 0)
            return ReadOnlySpan<float>.Empty;

        return data.Slice(data.Length - _vocabSize, _vocabSize);
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

    private static int? TryGetBosTokenId(Tokenizer tokenizer)
    {
        try
        {
            int id = tokenizer.GetBosTokenId();
            return id >= 0 ? id : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryReadVocabSize(string modelDir)
    {
        foreach (string name in new[] { "genai_config.json", "config.json" })
        {
            string path = Path.Combine(modelDir, name);
            if (!File.Exists(path))
                continue;

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (TryFindIntProperty(doc.RootElement, "vocab_size", out int value) ||
                TryFindIntProperty(doc.RootElement, "vocabSize", out value))
                return value;
        }

        return null;
    }

    private static string? TryReadChatTemplate(string modelDir)
    {
        string path = Path.Combine(modelDir, "chat_template.jinja");
        if (!File.Exists(path))
            return null;

        return File.ReadAllText(path);
    }

    private static bool TryFindIntProperty(JsonElement element, string name, out int value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.Ordinal) &&
                    property.Value.TryGetInt32(out value))
                    return true;

                if (TryFindIntProperty(property.Value, name, out value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                if (TryFindIntProperty(item, name, out value))
                    return true;
        }

        value = 0;
        return false;
    }

    public void Dispose()
    {
        _tokenizer.Dispose();
        _model.Dispose();
        _ogaHandle.Dispose();
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

    private sealed record ChatTemplateMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
