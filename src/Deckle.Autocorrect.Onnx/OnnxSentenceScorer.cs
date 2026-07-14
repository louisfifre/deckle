using System.IO;
using System.Text;
using System.Threading;
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
    private readonly Config _config;
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly double _margin;
    private readonly int _vocabSize;
    private readonly int? _bosTokenId;
    private readonly string? _chatTemplate;
    private readonly string _executionProvider;

    // The execution provider the judge model was loaded onto ("dml" for the GPU,
    // "cpu" for the built-in CPU EP) — surfaced so a run can report where it ran.
    public string ExecutionProvider => _executionProvider;

    public OnnxSentenceScorer(string modelDir, double margin, string executionProvider = "dml")
    {
        _margin = double.IsFinite(margin) && margin > 0.0 ? margin : 0.0;
        _vocabSize = TryReadVocabSize(modelDir) ?? 0;
        _chatTemplate = TryReadChatTemplate(modelDir);
        _executionProvider = string.IsNullOrWhiteSpace(executionProvider)
            ? "cpu"
            : executionProvider.Trim();

        OgaHandle? ogaHandle = null;
        Config? config = null;
        Model? model = null;
        Tokenizer? tokenizer = null;
        try
        {
            ogaHandle = new OgaHandle();

            (config, model) = CreateModel(modelDir, _executionProvider);
            tokenizer = new Tokenizer(model);

            _ogaHandle = ogaHandle;
            _config = config;
            _model = model;
            _tokenizer = tokenizer;
            _bosTokenId = TryGetBosTokenId(_tokenizer);
        }
        catch
        {
            tokenizer?.Dispose();
            model?.Dispose();
            config?.Dispose();
            ogaHandle?.Dispose();
            throw;
        }
    }

    // Builds the config and the model, with one bounded retry. The provider is
    // chosen in code, not read from the export's genai_config.json, so one CPU
    // int4 export can be driven onto the GPU (DirectML) without a re-export:
    // clear the config's providers and append the chosen one. "cpu" leaves the
    // list empty → the built-in CPU EP. Model construction enumerates the DML
    // devices, and that enumeration fails transiently — measured on the test
    // host (2026-07-14): "Specified provider is not supported" on one run,
    // clean on the next, same binary and machine. One retry absorbs the flake
    // for every consumer (live composition, probe, replay); a second failure
    // is a real one and propagates. The config is rebuilt per attempt rather
    // than reused across a failed native construction.
    private static (Config Config, Model Model) CreateModel(string modelDir, string executionProvider)
    {
        for (int attempt = 0; ; attempt++)
        {
            Config? config = null;
            try
            {
                config = new Config(modelDir);
                config.ClearProviders();
                if (!string.Equals(executionProvider, "cpu", StringComparison.OrdinalIgnoreCase))
                    config.AppendProvider(executionProvider);

                return (config, new Model(config));
            }
            catch (OnnxRuntimeGenAIException) when (attempt == 0)
            {
                config?.Dispose();
                Thread.Sleep(250);
            }
            catch
            {
                config?.Dispose();
                throw;
            }
        }
    }

    public static ISentenceScorer? TryLoad(string modelDir, double margin, string executionProvider = "dml")
    {
        try
        {
            if (!Directory.Exists(modelDir))
                return null;

            return new OnnxSentenceScorer(modelDir, margin, executionProvider);
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

    // Reads one vocabulary-wide logits row, upcast to float. The judge's logits come
    // back float32 from the CPU int4 export but float16 from the DirectML export — a
    // `-e dml` build forces a FLOAT16 io dtype (there is no FP32 DML path) — so the
    // row is read in its own element type. The per-row allocation is dwarfed by the
    // forward pass it follows.
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
        _config.Dispose();
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
