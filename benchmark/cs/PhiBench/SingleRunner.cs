using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Benchmark.PhiBench;

/// <summary>
/// Handles <c>PhiBench single ...</c> — one transcription, JSON result on
/// stdout. Used for palier 1 sanity checks (GPU placement, VRAM, RTF on a
/// single sample).
/// </summary>
public static class SingleRunner
{
    public static async Task<int> RunAsync(Dictionary<string, string> opts)
    {
        var modelPath = Require(opts, "model-path");
        var audioPath = Require(opts, "audio");
        var prompt = opts.GetValueOrDefault("prompt", "");
        var systemPrompt = opts.GetValueOrDefault("system-prompt", "");
        var maxTokens = int.Parse(opts.GetValueOrDefault("max-new-tokens", "1024"));
        var executionProvider = opts.GetValueOrDefault("execution-provider", "dml");

        if (!File.Exists(audioPath))
            throw new ArgumentException($"Audio file not found: {audioPath}");

        Console.Error.WriteLine($"=== PhiBench single ===");
        Console.Error.WriteLine($"  model_path : {modelPath}");
        Console.Error.WriteLine($"  audio      : {audioPath}");
        Console.Error.WriteLine($"  ep         : {executionProvider}");
        Console.Error.WriteLine($"  prompt     : {(string.IsNullOrEmpty(prompt) ? "<default FR transcription>" : prompt)}");

        using var ogaHandle = new OgaHandle();
        Console.Error.WriteLine($"  loading model...");
        using var transcriber = new Phi4Transcriber(modelPath, executionProvider);
        Console.Error.WriteLine($"  model ready");

        Console.Error.WriteLine($"  transcribing...");
        var result = transcriber.Transcribe(
            audioPath: audioPath,
            userPrompt: prompt,
            systemPrompt: string.IsNullOrEmpty(systemPrompt) ? null : systemPrompt,
            maxNewTokens: maxTokens);

        var output = new Dictionary<string, object?>
        {
            ["text"]             = result.Text,
            ["elapsed_s"]        = result.ElapsedSeconds,
            ["audio_s"]          = result.AudioSeconds,
            ["rtf"]              = result.Rtf,
            ["generated_tokens"] = result.GeneratedTokens,
            ["ok"]               = result.Ok,
            ["error"]            = result.Error,
            ["extras"]           = result.Extras,
        };
        Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        }));
        await Task.CompletedTask;
        return result.Ok ? 0 : 1;
    }

    private static string Require(Dictionary<string, string> opts, string key)
    {
        if (!opts.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
            throw new ArgumentException($"Missing required option --{key}");
        return value;
    }
}
