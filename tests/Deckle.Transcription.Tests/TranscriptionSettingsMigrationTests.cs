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

    private static JsonObject Streaming(string json)
        => (JsonObject)JsonNode.Parse(json)!["streaming"]!;
}
