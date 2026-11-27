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

        Assert.Equal("", started.ChangedSettings(AmbientStartSettings.From(settings)));
    }

    [Fact]
    public void TheMonitorIsNamedWhenItChanges()
    {
        var settings = new AmbientSettings();
        var started = AmbientStartSettings.From(settings);

        settings.SelectedMonitorDeviceName = @"\\.\DISPLAY2";

        Assert.Equal(
            "SelectedMonitorDeviceName",
            started.ChangedSettings(AmbientStartSettings.From(settings)));
    }

    // A zone pick or a per-light brightness is read on every tick, so a
    // change to one never restarts the run.
    [Fact]
    public void AZonePickDoesNotRestartTheRun()
    {
        var settings = new AmbientSettings();
        var started = AmbientStartSettings.From(settings);

        settings.LightZones = new Dictionary<string, LightZone> { ["1"] = LightZone.Left };

        Assert.Equal("", started.ChangedSettings(AmbientStartSettings.From(settings)));
    }
}
