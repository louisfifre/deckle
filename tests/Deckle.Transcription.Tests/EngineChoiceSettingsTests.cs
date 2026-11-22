using System.Text.Json;
using Deckle.Core;
using Xunit;

namespace Deckle.Transcription.Tests;

// The engine settings as the settings file stores them. Parakeet is the only
// engine, so the file carries no engine choice; a file written while Whisper was
// an option still holds that choice and its Whisper-only values, and must load
// with every other value intact.
[Trait("Category", "unit")]
public sealed class EngineChoiceSettingsTests : IDisposable
{
    // The options the settings service persists with.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "deckle-engine-settings-" + Guid.NewGuid().ToString("N"));

    public EngineChoiceSettingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_settings_file_without_engine_values_runs_Parakeet_with_an_FP32_encoder_on_the_GPU()
    {
        var settings = JsonSerializer.Deserialize<TranscriptionSettings>("""{ "engine": { } }""", Options)!;

        Assert.Equal(ParakeetEncoderPrecision.Fp32, settings.Engine.ParakeetEncoder);
        Assert.Equal("", settings.Engine.ParakeetModelDirectory);
        Assert.True(settings.Engine.UseGpu);
    }

    [Fact]
    public void The_encoder_precision_is_written_by_name_and_read_back()
    {
        var settings = new TranscriptionSettings();
        settings.Engine.ParakeetEncoder = ParakeetEncoderPrecision.Fp16;
        settings.Engine.ParakeetModelDirectory = @"C:\models\parakeet";

        string json = JsonSerializer.Serialize(settings, Options);
        var back = JsonSerializer.Deserialize<TranscriptionSettings>(json, Options)!;

        Assert.Contains("\"Fp16\"", json);
        Assert.Equal(ParakeetEncoderPrecision.Fp16, back.Engine.ParakeetEncoder);
        Assert.Equal(@"C:\models\parakeet", back.Engine.ParakeetModelDirectory);
    }

    [Theory]
    [InlineData("Whisper")]
    [InlineData("Parakeet")]
    public void A_file_written_with_an_engine_choice_loads_with_every_other_value_intact(string backend)
    {
        string path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, $$"""
            {
              "modelsDirectory": "C:\\speech",
              "engine": {
                "backend": "{{backend}}",
                "parakeetModelDirectory": "C:\\speech\\parakeet",
                "parakeetEncoder": "Fp16",
                "model": "ggml-small.bin",
                "useGpu": false,
                "language": "en",
                "initialPrompt": "A prompt.",
                "carryInitialPrompt": false
              },
              "confidence": { "entropyThreshold": 3.0 },
              "outputFilters": { "suppressBlank": false },
              "decoding": { "beamSize": 8 },
              "context": { "useContext": false },
              "streaming": { "strategy": 0, "minSpeechMs": 400 },
              "lexicon": { "terms": ["Deckle", "WinUI"], "boostEnabled": false }
            }
            """);

        using var store = new JsonSettingsStore<TranscriptionSettings>(
            path, @"Deckle-Test-EngineChoice-" + Guid.NewGuid().ToString("N"), Options);
        TranscriptionSettings loaded = store.Current;

        Assert.Equal(@"C:\speech", loaded.ModelsDirectory);
        Assert.Equal(@"C:\speech\parakeet", loaded.Engine.ParakeetModelDirectory);
        Assert.Equal(ParakeetEncoderPrecision.Fp16, loaded.Engine.ParakeetEncoder);
        Assert.False(loaded.Engine.UseGpu);
        Assert.Equal(PipelineStrategyKind.Monolithic, loaded.Streaming.Strategy);
        Assert.Equal(400, loaded.Streaming.MinSpeechMs);
        Assert.Equal(new[] { "Deckle", "WinUI" }, loaded.Lexicon.Terms);
        Assert.False(loaded.Lexicon.BoostEnabled);
    }

    [Fact]
    public void The_next_save_of_an_old_file_drops_the_values_no_engine_reads()
    {
        string path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """
            { "engine": { "backend": "Whisper", "model": "ggml-base.bin", "language": "fr", "useGpu": false } }
            """);

        using (var store = new JsonSettingsStore<TranscriptionSettings>(
            path, @"Deckle-Test-EngineChoice-" + Guid.NewGuid().ToString("N"), Options))
        {
            store.Flush();
        }

        string rewritten = File.ReadAllText(path);
        Assert.DoesNotContain("backend", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ggml-base.bin", rewritten);
        Assert.DoesNotContain("\"language\"", rewritten);
        Assert.Contains("\"useGpu\": false", rewritten);
    }
}
