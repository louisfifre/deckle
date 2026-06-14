using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Deckle.Benchmark.PhiBench.Models;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Benchmark.PhiBench;

/// <summary>
/// Wraps a long-lived Phi-4-multimodal ONNX model on DirectML. The model is
/// loaded once in the constructor (paid cost — several seconds, ~4 GiB VRAM
/// for the gpu-int4-rtn-block-32 variant) and reused across many transcribe
/// calls. Mirrors the lifecycle of <c>VoxtralTransformersSource</c> in the
/// Python bench (benchmark/lib/sources/voxtral_transformers.py).
///
/// The chat template is the one shipped with the model under
/// <c>chat_template.jinja</c> — applied via <c>tokenizer.ApplyChatTemplate</c>
/// over a messages list <c>[{role: system, content: ...}, {role: user, content: ...}]</c>.
///
/// Phi-4 has no canonical transcription mode (unlike Voxtral's [TRANSCRIBE]
/// token). The bench passes the regime prompt through as-is. A null user
/// prompt falls back to a short FR instruction; an empty string is honored.
/// </summary>
public sealed class Phi4Transcriber : IDisposable
{
    // Aligned with the prompt formulation seen in microsoft/Phi-4-multimodal-instruct
    // examples for speech recognition (paper arXiv 2503.01743, model card snippets).
    // The instruction stays short so as not to dilute the audio attention budget.
    private const string DefaultUserPrompt = "Transcribe the audio clip into text in French.";

    private readonly Config _config;
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;
    private readonly TokenizerStream _tokenizerStream;
    private readonly MultiModalProcessor _processor;
    private readonly string _modelPath;
    private readonly string _executionProvider;

    public Phi4Transcriber(string modelPath, string executionProvider = "dml")
    {
        if (!Directory.Exists(modelPath))
            throw new DirectoryNotFoundException($"Model folder not found: {modelPath}");

        _modelPath = modelPath;
        _executionProvider = executionProvider;

        _config = new Config(modelPath);
        _config.ClearProviders();
        if (!string.Equals(executionProvider, "cpu", StringComparison.OrdinalIgnoreCase))
        {
            _config.AppendProvider(executionProvider);
        }

        _model = new Model(_config);
        _tokenizer = new Tokenizer(_model);
        _tokenizerStream = _tokenizer.CreateStream();
        _processor = new MultiModalProcessor(_model);
    }

    public string ModelPath => _modelPath;
    public string ExecutionProvider => _executionProvider;

    /// <summary>
    /// Transcribes one audio file. <paramref name="audioSeconds"/> is provided
    /// from the corpus when available, computed from the WAV header otherwise.
    /// On error returns <c>Ok=false</c> with the message in <c>Error</c> — the
    /// bench loop continues on the next row.
    /// </summary>
    public TranscriptionResult Transcribe(
        string audioPath,
        string? userPrompt,
        string? systemPrompt,
        int maxNewTokens,
        double? audioSecondsOverride = null)
    {
        var audioSeconds = audioSecondsOverride ?? WavHeader.GetDurationSeconds(audioPath);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // null userPrompt → fall back to default. Empty string is honored
            // verbatim — useful for the issue #1455 mitigation where an empty
            // post-audio prompt is documented to behave differently from a
            // populated instruction.
            var promptForUser = userPrompt ?? DefaultUserPrompt;
            // Hand-build the Phi-4 prompt to bypass any chat-template encoding
            // surprises (e.g. JSON Unicode-escaping the < and | characters of
            // the <|audio_1|> token before the template engine sees them).
            // Format from tokenizer_config.json chat_template:
            //   <|system|>{sys}<|end|><|user|>{content}<|end|><|assistant|>
            var templated = BuildRawPhi4Prompt(promptForUser, systemPrompt);

            using var audios = Audios.Load(new[] { audioPath });
            // Audio-only path : ProcessAudios is the dedicated overload, distinct
            // from ProcessImagesAndAudios(prompt, null, audios) which leaves the
            // images branch in the multimodal pipeline. Empirically the
            // image-and-audio variant with null images on Phi-4 caused the model
            // to reply "I cannot transcribe the audio you mentioned" — the audio
            // embeddings weren't being spliced in at the <|audio_1|> position.
            using var inputTensors = _processor.ProcessAudios(templated, audios);

            using var generatorParams = new GeneratorParams(_model);
            // Phi-4 multimodal expects max_length as total context (input + output).
            // The Python sample (examples/python/model-mm.py) defaults to 7680.
            // We set a generous upper bound here and rely on the EOS token to stop
            // generation — early truncation produces incoherent loops on short audios.
            generatorParams.SetSearchOption("max_length", Math.Max(2048, maxNewTokens + 1024));
            generatorParams.SetSearchOption("do_sample", false);

            using var generator = new Generator(_model, generatorParams);
            generator.SetInputs(inputTensors);

            var sb = new StringBuilder();
            int generatedTokens = 0;
            while (!generator.IsDone())
            {
                generator.GenerateNextToken();
                var next = generator.GetNextTokens();
                if (next.Length == 0) break;
                sb.Append(_tokenizerStream.Decode(next[0]));
                generatedTokens++;
            }

            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed.TotalSeconds;

            return new TranscriptionResult(
                Text: sb.ToString().Trim(),
                ElapsedSeconds: elapsed,
                AudioSeconds: audioSeconds,
                Rtf: audioSeconds > 0 ? elapsed / audioSeconds : 0.0,
                GeneratedTokens: generatedTokens,
                Ok: true,
                Error: string.Empty,
                Extras: new Dictionary<string, object?>
                {
                    ["model_path"]     = _modelPath,
                    ["execution_provider"] = _executionProvider,
                    ["max_new_tokens"] = maxNewTokens,
                    ["mode"]           = "apply_chat_template",
                    ["system_prompt"]  = systemPrompt ?? string.Empty,
                    ["user_prompt"]    = promptForUser,
                });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            return new TranscriptionResult(
                Text: string.Empty,
                ElapsedSeconds: elapsed,
                AudioSeconds: audioSeconds,
                Rtf: audioSeconds > 0 ? elapsed / audioSeconds : 0.0,
                GeneratedTokens: -1,
                Ok: false,
                Error: $"{ex.GetType().Name}: {ex.Message}",
                Extras: new Dictionary<string, object?>());
        }
    }

    /// <summary>Warmup transcription on a single audio. Pays the first-call JIT
    /// cost outside the measurement loop. Errors are swallowed — warmup is
    /// best-effort.</summary>
    public void Warmup(string audioPath)
    {
        try
        {
            _ = Transcribe(audioPath, DefaultUserPrompt, systemPrompt: null, maxNewTokens: 16);
        }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        _processor.Dispose();
        _tokenizerStream.Dispose();
        _tokenizer.Dispose();
        _model.Dispose();
        _config.Dispose();
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private static string BuildRawPhi4Prompt(string userPrompt, string? systemPrompt)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            sb.Append("<|system|>").Append(systemPrompt).Append("<|end|>");
        }
        sb.Append("<|user|><|audio_1|>").Append(userPrompt).Append("<|end|><|assistant|>");
        return sb.ToString();
    }

    private static List<Dictionary<string, string>> BuildMessages(string userPrompt, string? systemPrompt)
    {
        var messages = new List<Dictionary<string, string>>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new Dictionary<string, string>
            {
                ["role"] = "system",
                ["content"] = systemPrompt,
            });
        }
        // Phi-4 multimodal user content prefixes audio tags before the text instruction
        // (cf. Common.GetUserContent for "phi4mm" in onnxruntime-genai samples).
        messages.Add(new Dictionary<string, string>
        {
            ["role"] = "user",
            ["content"] = "<|audio_1|>\n" + userPrompt,
        });
        return messages;
    }

    private string ApplyChatTemplate(List<Dictionary<string, string>> messages)
    {
        var json = JsonSerializer.Serialize(messages);
        var jinjaPath = Path.Combine(_modelPath, "chat_template.jinja");
        var templateStr = File.Exists(jinjaPath) ? File.ReadAllText(jinjaPath, Encoding.UTF8) : string.Empty;
        return _tokenizer.ApplyChatTemplate(
            messages: json,
            tools: string.Empty,
            add_generation_prompt: true,
            template_str: templateStr);
    }

    /// <summary>Rough character-based estimate of prompt token count, used to
    /// size <c>max_length</c> as <c>promptTokens + maxNewTokens</c>. Conservative —
    /// 1 token ≈ 3 chars for English/French, audio tokens added on top once we
    /// know the audio. We add a generous margin (audioSeconds * 50) to cover the
    /// audio tokens injected by the multimodal processor.</summary>
    private static int GuessPromptTokenCount(string prompt) => Math.Max(64, prompt.Length / 3 + 256);
}
