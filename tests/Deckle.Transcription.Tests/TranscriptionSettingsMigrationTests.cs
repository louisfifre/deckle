using System.Text.Json;
using System.Text.Json.Nodes;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "unit")]
public class TranscriptionSettingsMigrationTests
{
    [Fact]
    public void MigratesOldRuntimeSegmenterDefaults()
    {
        var settings = new TranscriptionSettings();
        settings.Streaming.Segmenter.HangoverMaxMs = 10_000;
        settings.Streaming.Segmenter.HangoverMinMs = 500;
        settings.Streaming.Segmenter.HangoverRampStartMs = 15_000;
        settings.Streaming.Segmenter.HangoverRampEndMs = 120_000;

        bool migrated = TranscriptionSettingsService.ApplyPostLoadMigrations(settings);

        Assert.True(migrated);
        Assert.Equal(5_000, settings.Streaming.Segmenter.HangoverMaxMs);
        Assert.Equal(15_000, settings.Streaming.Segmenter.HangoverRampStartMs);
        Assert.Equal(120_000, settings.Streaming.Segmenter.HangoverRampEndMs);
    }

    [Fact]
    public void RevertsBadSixtySecondRampStartMigration()
    {
        var settings = new TranscriptionSettings();
        settings.Streaming.Segmenter.HangoverMaxMs = 5_000;
        settings.Streaming.Segmenter.HangoverMinMs = 500;
        settings.Streaming.Segmenter.HangoverRampStartMs = 60_000;
        settings.Streaming.Segmenter.HangoverRampEndMs = 120_000;

        bool migrated = TranscriptionSettingsService.ApplyPostLoadMigrations(settings);

        Assert.True(migrated);
        Assert.Equal(15_000, settings.Streaming.Segmenter.HangoverRampStartMs);
        Assert.Equal(120_000, settings.Streaming.Segmenter.HangoverRampEndMs);
    }

    [Fact]
    public void DoesNotMigrateCustomizedCurve()
    {
        var settings = new TranscriptionSettings();
        settings.Streaming.Segmenter.HangoverMaxMs = 5_000;
        settings.Streaming.Segmenter.HangoverMinMs = 500;
        settings.Streaming.Segmenter.HangoverRampStartMs = 60_000;
        settings.Streaming.Segmenter.HangoverRampEndMs = 120_000;
        settings.Streaming.Segmenter.HangoverCurveY1 = 0.20;

        bool migrated = TranscriptionSettingsService.ApplyPostLoadMigrations(settings);

        Assert.False(migrated);
        Assert.Equal(60_000, settings.Streaming.Segmenter.HangoverRampStartMs);
    }

    // The segmenter's minimum utterance and Silero's minimum speech were one
    // floor stored twice; streaming.minSpeechMs replaces both.

    [Fact]
    public void MinSpeechTakesSilerosValueWhenBothOldFloorsDiffer()
    {
        const string json = """
            { "streaming": {
                "segmenter":  { "marginMs": 150, "minUtteranceMs": 300 },
                "speechTrim": { "enabled": true, "minSpeechDurationMs": 400 } } }
            """;

        (string migratedJson, bool migrated) = TranscriptionSettingsService.MigrateMinSpeech(json);

        Assert.True(migrated);
        JsonObject streaming = Streaming(migratedJson);
        Assert.Equal(400, (int)streaming["minSpeechMs"]!);
        Assert.False(((JsonObject)streaming["segmenter"]!).ContainsKey("minUtteranceMs"));
        Assert.False(((JsonObject)streaming["speechTrim"]!).ContainsKey("minSpeechDurationMs"));
        Assert.Equal(150, (int)streaming["segmenter"]!["marginMs"]!);
        Assert.True((bool)streaming["speechTrim"]!["enabled"]!);
    }

    // A real file carries both old keys, since the store writes the whole
    // POCO: the one the person edited is the one off the shipped default.
    [Fact]
    public void MinSpeechTakesTheSegmentersValueWhenOnlyItDiffersFromTheDefault()
    {
        int shipped = new StreamingSettings().MinSpeechMs;
        int edited = shipped + 150;
        string json = $$"""
            { "streaming": {
                "segmenter":  { "minUtteranceMs": {{edited}} },
                "speechTrim": { "minSpeechDurationMs": {{shipped}} } } }
            """;

        (string migratedJson, bool migrated) = TranscriptionSettingsService.MigrateMinSpeech(json);

        Assert.True(migrated);
        Assert.Equal(edited, (int)Streaming(migratedJson)["minSpeechMs"]!);
    }

    [Fact]
    public void MinSpeechTakesTheSegmentersValueWhenItIsTheOnlyOldFloor()
    {
        const string json = """
            { "streaming": { "segmenter": { "minUtteranceMs": 300 } } }
            """;

        (string migratedJson, bool migrated) = TranscriptionSettingsService.MigrateMinSpeech(json);

        Assert.True(migrated);
        JsonObject streaming = Streaming(migratedJson);
        Assert.Equal(300, (int)streaming["minSpeechMs"]!);
        Assert.False(((JsonObject)streaming["segmenter"]!).ContainsKey("minUtteranceMs"));
    }

    [Fact]
    public void AFileWithNeitherOldFloorIsLeftUntouched()
    {
        const string json = """
            { "streaming": { "segmenter": { "marginMs": 150 }, "speechTrim": { "enabled": true } } }
            """;

        (string migratedJson, bool migrated) = TranscriptionSettingsService.MigrateMinSpeech(json);

        Assert.False(migrated);
        Assert.Same(json, migratedJson);
    }

    [Fact]
    public void AnExistingMinSpeechIsKeptAndTheOldFloorsAreRemoved()
    {
        const string json = """
            { "streaming": {
                "minSpeechMs": 200,
                "segmenter":  { "minUtteranceMs": 300 },
                "speechTrim": { "minSpeechDurationMs": 400 } } }
            """;

        (string migratedJson, bool migrated) = TranscriptionSettingsService.MigrateMinSpeech(json);

        Assert.True(migrated);
        JsonObject streaming = Streaming(migratedJson);
        Assert.Equal(200, (int)streaming["minSpeechMs"]!);
        Assert.False(((JsonObject)streaming["segmenter"]!).ContainsKey("minUtteranceMs"));
        Assert.False(((JsonObject)streaming["speechTrim"]!).ContainsKey("minSpeechDurationMs"));
    }

    // The cut preset selection and its custom values (cutPreset, cutCustomValues)
    // were removed from the segmenter settings. A file written by an older build
    // still carries them; System.Text.Json skips unknown properties by default,
    // so the file must load and keep the values it still knows.
    [Fact]
    public void AFileCarryingTheRemovedCutPresetFieldsStillLoads()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        const string legacy = """
            { "streaming": { "segmenter": {
                "marginMs": 200,
                "cutPreset": "custom",
                "cutCustomValues": { "hangoverMaxMs": 8000, "hangoverMinMs": 1000,
                    "hangoverRampStartMs": 30000, "hangoverRampEndMs": 180000,
                    "curveX1": 0.85, "curveY1": 0.10, "curveX2": 0.90, "curveY2": 0.25 } } } }
            """;

        var restored = JsonSerializer.Deserialize<TranscriptionSettings>(legacy, options);

        Assert.NotNull(restored);
        Assert.Equal(200, restored.Streaming.Segmenter.MarginMs);
    }
    private static JsonObject Streaming(string json)
        => (JsonObject)JsonNode.Parse(json)!["streaming"]!;
}
