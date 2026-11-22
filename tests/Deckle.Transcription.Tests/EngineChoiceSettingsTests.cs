using System.Text.Json;
using Xunit;

namespace Deckle.Transcription.Tests;

// The speech engine choice as the settings file stores it. The Transcription page
// writes it, the composition root reads it at startup, and a file written before
// the choice existed runs Parakeet, the engine a fresh install downloads.
[Trait("Category", "unit")]
public sealed class EngineChoiceSettingsTests
{
    // The options the settings service persists with.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void A_settings_file_without_the_engine_choice_runs_Parakeet_with_an_FP32_encoder()
    {
        var settings = JsonSerializer.Deserialize<TranscriptionSettings>(
            """{ "engine": { "model": "ggml-base.bin" } }""", Options)!;

        Assert.Equal(AsrBackendKind.Parakeet, settings.Engine.Backend);
        Assert.Equal(ParakeetEncoderPrecision.Fp32, settings.Engine.ParakeetEncoder);
        Assert.Equal("", settings.Engine.ParakeetModelDirectory);
    }

    [Fact]
    public void The_engine_choice_is_written_by_name_and_read_back()
    {
        var settings = new TranscriptionSettings();
        settings.Engine.Backend = AsrBackendKind.Whisper;
        settings.Engine.ParakeetEncoder = ParakeetEncoderPrecision.Fp16;
        settings.Engine.ParakeetModelDirectory = @"C:\models\parakeet";

        string json = JsonSerializer.Serialize(settings, Options);
        var back = JsonSerializer.Deserialize<TranscriptionSettings>(json, Options)!;

        Assert.Contains("\"Whisper\"", json);
        Assert.Contains("\"Fp16\"", json);
        Assert.Equal(AsrBackendKind.Whisper, back.Engine.Backend);
        Assert.Equal(ParakeetEncoderPrecision.Fp16, back.Engine.ParakeetEncoder);
        Assert.Equal(@"C:\models\parakeet", back.Engine.ParakeetModelDirectory);
    }
}
