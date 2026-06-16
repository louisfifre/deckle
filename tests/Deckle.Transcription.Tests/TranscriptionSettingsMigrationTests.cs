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
}
