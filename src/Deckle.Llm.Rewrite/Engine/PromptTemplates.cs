namespace Deckle.Llm.Rewrite;

// ─── Prompt templates by model family ────────────────────────────────────────
//
// Ollama accepts a "raw" mode in /api/generate: the prompt is sent as-is to
// the tokenizer, without Modelfile TEMPLATE substitution. In return, the caller
// must produce a prompt in the format expected by the family.
//
// This class detects the family from the model name, then produces:
//   - the formatted prompt (with system + user merged according to family convention),
//   - stop tokens to send as options (end-of-turn, etc.),
//   - a family identifier for logs.
//
// Formats are those documented by each publisher. BOS (<s>, <|begin_of_text|>)
// is added automatically by the server-side tokenizer; do not prefix it.
//
// Supported families (case-insensitive substring match on the name):
//   - mistral / ministral / mixtral       → [INST] ... [/INST]
//   - llama3, llama-3, meta-llama-3       → header_id blocks + <|eot_id|>
//   - qwen (qwen2, qwen2.5, qwen3)        → ChatML <|im_start|> / <|im_end|>
//   - gemma (gemma2, gemma3)              → <start_of_turn>user / <end_of_turn>
//   - phi3 / phi-3 / phi4                 → <|system|> <|user|> <|assistant|>
//   - fallback (unknown family)           → ChatML (covers many recent models,
//     including yi, deepseek, nous-*, openchat)

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
    /// Cleans model output from stop tokens that sometimes trail at the end of
    /// the string (some runtimes include them in returned text).
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

    // Mistral / Ministral / Mixtral: system merged into the first [INST].
    // Format officiel : "[INST] {system}\n\n{user} [/INST]"
    static string MistralInst(string system, string user)
    {
        string merged = string.IsNullOrWhiteSpace(system) ? user : $"{system}\n\n{user}";
        return $"[INST] {merged} [/INST]";
    }

    // Llama 3 / 3.1 / 3.2: header_id blocks separated by <|eot_id|>, ending
    // with an empty assistant block to prime generation.
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

    // ChatML: Qwen, Yi, DeepSeek, Nous-*, OpenChat, most recent models outside
    // Mistral/Llama/Gemma.
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

    // Gemma 2 / 3: no native system role; system is merged at the beginning of
    // the user turn. <start_of_turn> / <end_of_turn> markers.
    static string Gemma(string system, string user)
    {
        string merged = string.IsNullOrWhiteSpace(system) ? user : $"{system}\n\n{user}";
        return $"<start_of_turn>user\n{merged}<end_of_turn>\n<start_of_turn>model\n";
    }

    // Phi-3 / Phi-4: <|system|> <|user|> <|assistant|> blocks terminated by <|end|>.
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
