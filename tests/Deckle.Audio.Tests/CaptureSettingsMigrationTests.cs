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

    // The microphone chosen under the waveIn capture is a device index; the
    // PortAudio capture names it by endpoint id. The person's choice must
    // survive the upgrade, and the index must not be written back.
    [Fact]
    public void ALegacyMicrophoneIndexBecomesItsEndpointId()
    {
        var settings = new CaptureSettings { SchemaVersion = 1, AudioInputDeviceId = 2 };

        bool migrated = CaptureSettingsService.ApplyPostLoadMigrations(
            settings, index => index == 2 ? "{0.0.1.00000000}.{mic-2}" : null);

        Assert.True(migrated);
        Assert.Equal("{0.0.1.00000000}.{mic-2}", settings.AudioInputEndpointId);
        Assert.Null(settings.AudioInputDeviceId);
        Assert.Equal(CaptureSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void TheLegacySystemDefaultStaysTheSystemDefault()
    {
        var settings = new CaptureSettings { SchemaVersion = 1, AudioInputDeviceId = -1 };

        CaptureSettingsService.ApplyPostLoadMigrations(
            settings, _ => throw new InvalidOperationException("the default needs no lookup"));

        Assert.Equal("", settings.AudioInputEndpointId);
        Assert.Null(settings.AudioInputDeviceId);
    }

    [Fact]
    public void ALegacyIndexThatMatchesNoDeviceFallsBackToTheSystemDefault()
    {
        var settings = new CaptureSettings { SchemaVersion = 1, AudioInputDeviceId = 5 };

        bool migrated = CaptureSettingsService.ApplyPostLoadMigrations(settings, _ => null);

        Assert.True(migrated);
        Assert.Equal("", settings.AudioInputEndpointId);
        Assert.Null(settings.AudioInputDeviceId);
    }

    [Fact]
    public void ExclusiveModeIsOffByDefault()
    {
        Assert.False(new CaptureSettings().ExclusiveMode);
    }
}
