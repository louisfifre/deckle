using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientStartSettingsTests
{
    [Fact]
    public void UnchangedStartSettingsNameNothing()
    {
        var settings = new AmbientSettings();

        var started = AmbientStartSettings.From(settings, performanceMode: false);
        settings.SmoothingAlpha = 0.5;
        settings.ChangeThreshold = 20;

        Assert.Equal("", started.ChangedSettings(AmbientStartSettings.From(settings, performanceMode: false)));
    }

    [Fact]
    public void EachStartSettingIsNamedWhenItChanges()
    {
        var settings = new AmbientSettings();
        var started = AmbientStartSettings.From(settings, performanceMode: false);

        settings.SelectedMonitorDeviceName = @"\\.\DISPLAY2";
        settings.UseMultiLight = !settings.UseMultiLight;

        Assert.Equal(
            "SelectedMonitorDeviceName,UseMultiLight,PerformanceMode",
            started.ChangedSettings(AmbientStartSettings.From(settings, performanceMode: true)));
    }

    [Fact]
    public void TurningThePerformanceModeOnOrOffIsAStartChange()
    {
        var settings = new AmbientSettings();

        var normal = AmbientStartSettings.From(settings, performanceMode: false);
        var lowPower = AmbientStartSettings.From(settings, performanceMode: true);

        Assert.Equal("PerformanceMode", normal.ChangedSettings(lowPower));
        Assert.Equal("PerformanceMode", lowPower.ChangedSettings(normal));
    }
}
