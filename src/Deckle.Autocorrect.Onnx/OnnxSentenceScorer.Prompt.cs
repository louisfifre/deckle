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
            catch (Exception ex)
            {
                // Some exported tokenizers carry incomplete chat-template support.
                // The failure is deterministic for a model, so it is reported once
                // per load rather than on every sentence.
                if (Interlocked.Exchange(ref _chatTemplateFallbackReported, 1) == 0)
                    DeckleAutocorrectSource.Log.SentenceJudgeFeatureUnavailable(
                        DeckleAutocorrectSource.SentenceJudgeFeatures.ChatTemplate,
                        ex.GetType().FullName ?? ex.GetType().Name,
                        ex.Message);
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

    private sealed record ChatTemplateMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
