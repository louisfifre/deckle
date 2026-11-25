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
    public void EachStartSettingIsNamedWhenItChanges()
    {
        var settings = new AmbientSettings();
        var started = AmbientStartSettings.From(settings);

        settings.SelectedMonitorDeviceName = @"\\.\DISPLAY2";
        settings.UseMultiLight = !settings.UseMultiLight;

        Assert.Equal(
            "SelectedMonitorDeviceName,UseMultiLight",
            started.ChangedSettings(AmbientStartSettings.From(settings)));
    }
}
