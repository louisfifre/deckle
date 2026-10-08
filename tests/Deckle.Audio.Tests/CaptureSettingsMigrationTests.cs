using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

[Trait("Category", "unit")]
public class CaptureSettingsMigrationTests
{
    [Fact]
    public void FreshSettingsHaveNoRecordingLimit()
    {
        var settings = new CaptureSettings();

        Assert.Equal(0, settings.MaxRecordingDurationSeconds);
    }

    [Fact]
    public void UnversionedLegacyDefaultMigratesToNoLimit()
    {
        var settings = new CaptureSettings
        {
            MaxRecordingDurationSeconds = CaptureSettingsService.LegacyDefaultMaxRecordingDurationSeconds,
        };

        bool migrated = CaptureSettingsService.ApplyPostLoadMigrations(settings);

        Assert.True(migrated);
        Assert.Equal(0, settings.MaxRecordingDurationSeconds);
        Assert.Equal(CaptureSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void UnversionedChosenLimitIsKept()
    {
        var settings = new CaptureSettings { MaxRecordingDurationSeconds = 600 };

        bool migrated = CaptureSettingsService.ApplyPostLoadMigrations(settings);

        Assert.True(migrated);
        Assert.Equal(600, settings.MaxRecordingDurationSeconds);
        Assert.Equal(CaptureSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void VersionedTwentyMinuteChoiceIsNotMigratedAgain()
    {
        var settings = new CaptureSettings
        {
            SchemaVersion = CaptureSettings.CurrentSchemaVersion,
            MaxRecordingDurationSeconds = CaptureSettingsService.LegacyDefaultMaxRecordingDurationSeconds,
        };

        bool migrated = CaptureSettingsService.ApplyPostLoadMigrations(settings);

        Assert.False(migrated);
        Assert.Equal(
            CaptureSettingsService.LegacyDefaultMaxRecordingDurationSeconds,
            settings.MaxRecordingDurationSeconds);
    }

    [Fact]
    public void UnversionedNoLimitStaysNoLimitAndIsStamped()
    {
        var settings = new CaptureSettings { MaxRecordingDurationSeconds = 0 };

        bool migrated = CaptureSettingsService.ApplyPostLoadMigrations(settings);

        Assert.True(migrated);
        Assert.Equal(0, settings.MaxRecordingDurationSeconds);
        Assert.Equal(CaptureSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }
}
