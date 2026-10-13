namespace Deckle.Llm.Rewrite;

// ─── Prompt templates by model family ────────────────────────────────────────
//
// Ollama's "raw" mode in /api/generate sends the prompt as-is to the
// tokenizer, without Modelfile TEMPLATE substitution, so the caller must
// produce the format the model family expects.
//
// Formats are those documented by each publisher. The server-side tokenizer
// adds BOS (<s>, <|begin_of_text|>) itself; do not prefix it. An unknown
// family falls back to ChatML, which many recent models use (Yi, DeepSeek,
// Nous-*, OpenChat).

public static class PromptTemplates
{
    public static (string Prompt, string[] Stops, string Family) Build(string model, string system, string user)
    {
        string m = (model ?? "").ToLowerInvariant();

        if (Contains(m, "mistral", "ministral", "mixtral"))
            return (MistralInst(system, user), new[] { "[INST]", "[/INST]", "</s>" }, "mistral");

        if (Contains(m, "llama-3", "llama3", "meta-llama-3", "llama_3"))
            return (Llama3(system, user), new[] { "<|eot_id|>", "<|end_of_text|>" }, "llama3");

        if (Contains(m, "qwen"))
            return (ChatMl(system, user), new[] { "<|im_end|>", "<|endoftext|>" }, "qwen");

        if (Contains(m, "gemma"))
            return (Gemma(system, user), new[] { "<end_of_turn>", "<eos>" }, "gemma");

        if (Contains(m, "phi-3", "phi3", "phi-4", "phi4"))
            return (Phi(system, user), new[] { "<|end|>", "<|endoftext|>" }, "phi");

        return (ChatMl(system, user), new[] { "<|im_end|>", "<|endoftext|>" }, "chatml");
    }

    /// <summary>
    /// Some runtimes leave stop tokens in the returned text.
    /// </summary>
    public static string StripStops(string text, string family)
    {
        string[] tokens = family switch
        {
            "mistral" => new[] { "[INST]", "[/INST]", "</s>", "<s>" },
            "llama3"  => new[] { "<|eot_id|>", "<|end_of_text|>", "<|begin_of_text|>" },
            "qwen"    => new[] { "<|im_end|>", "<|im_start|>", "<|endoftext|>" },
            "gemma"   => new[] { "<end_of_turn>", "<start_of_turn>", "<eos>", "<bos>" },
            "phi"     => new[] { "<|end|>", "<|system|>", "<|user|>", "<|assistant|>", "<|endoftext|>" },
            _         => new[] { "<|im_end|>", "<|im_start|>", "<|endoftext|>" }
        };
        foreach (var t in tokens)
            text = text.Replace(t, "");
        return text;
    }

    static bool Contains(string haystack, params string[] needles)
    {
        foreach (var n in needles)
            if (haystack.Contains(n, StringComparison.Ordinal))
                return true;
        return false;
    }

    // ── Formats by family ─────────────────────────────────────────────────

    static string MistralInst(string system, string user)
    {
        string merged = string.IsNullOrWhiteSpace(system) ? user : $"{system}\n\n{user}";
        return $"[INST] {merged} [/INST]";
    }

    static string Llama3(string system, string user)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(system))
        {
            sb.Append("<|start_header_id|>system<|end_header_id|>\n\n");
            sb.Append(system);
            sb.Append("<|eot_id|>");
        }
        sb.Append("<|start_header_id|>user<|end_header_id|>\n\n");
        sb.Append(user);
        sb.Append("<|eot_id|>");
        sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
        return sb.ToString();
    }

    static string ChatMl(string system, string user)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(system))
        {
            sb.Append("<|im_start|>system\n");
            sb.Append(system);
            sb.Append("<|im_end|>\n");
        }
        sb.Append("<|im_start|>user\n");
        sb.Append(user);
        sb.Append("<|im_end|>\n");
        sb.Append("<|im_start|>assistant\n");
        return sb.ToString();
    }

    // Gemma has no native system role: the system prompt opens the user turn.
    static string Gemma(string system, string user)
    {
        string merged = string.IsNullOrWhiteSpace(system) ? user : $"{system}\n\n{user}";
        return $"<start_of_turn>user\n{merged}<end_of_turn>\n<start_of_turn>model\n";
    }

    static string Phi(string system, string user)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(system))
        {
            sb.Append("<|system|>\n");
            sb.Append(system);
            sb.Append("<|end|>\n");
        }
        sb.Append("<|user|>\n");
        sb.Append(user);
        sb.Append("<|end|>\n");
        sb.Append("<|assistant|>\n");
        return sb.ToString();
    }
}
