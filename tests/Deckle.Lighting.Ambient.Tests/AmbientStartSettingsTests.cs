using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientStartSettingsTests
{
    [Fact]
    public void UnchangedStartSettingsNameNothing()
    {
        var settings = new AmbientSettings();

        var started = AmbientStartSettings.From(settings);
        settings.SmoothingAlpha = 0.5;
        settings.ChangeThreshold = 20;

        Assert.Equal("", started.ChangedSettings(AmbientStartSettings.From(settings)));
    }

    [Fact]
    public void EachStartSettingIsNamedWhenItChanges()
    {
        var settings = new AmbientSettings();
        var started = AmbientStartSettings.From(settings);

        settings.SelectedMonitorDeviceName = @"\\.\DISPLAY2";
        settings.UseMultiLight = !settings.UseMultiLight;
        settings.CaptureFrameRateHz = 15;

        Assert.Equal(
            "SelectedMonitorDeviceName,UseMultiLight,CaptureFrameRateHz",
            started.ChangedSettings(AmbientStartSettings.From(settings)));
    }

    [Fact]
    public void AStoredRateThatSnapsToTheSameRateIsNotAChange()
    {
        var settings = new AmbientSettings { CaptureFrameRateHz = AmbientCaptureRate.Default };
        var started = AmbientStartSettings.From(settings);

        settings.CaptureFrameRateHz = AmbientCaptureRate.Default - 1;

        Assert.Equal("", started.ChangedSettings(AmbientStartSettings.From(settings)));
    }
}
