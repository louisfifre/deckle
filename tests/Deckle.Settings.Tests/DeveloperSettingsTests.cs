using System.Text.Json;
using Xunit;

namespace Deckle.Settings.Tests;

// The developer switch of the shell settings: off for a user who never touched
// it, and kept across a save and a load.
[Trait("Category", "unit")]
public sealed class DeveloperSettingsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void DeveloperModeIsOffByDefault()
    {
        Assert.False(new AppSettings().Developer.DeveloperMode);
    }

    [Fact]
    public void AFileWrittenBeforeTheSettingExistedLoadsWithDeveloperModeOff()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>("""{"appearance":{"theme":"Dark"}}""", Options)!;

        Assert.False(loaded.Developer.DeveloperMode);
    }

    // The Playground once had a switch of its own. A file that still carries
    // it loads, and the old switch does not turn Developer mode on.
    [Fact]
    public void AFileThatStillCarriesThePlaygroundSwitchLoadsWithDeveloperModeOff()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>(
            """{"developer":{"showPlayground":true}}""", Options)!;

        Assert.False(loaded.Developer.DeveloperMode);
    }

    [Fact]
    public void DeveloperModeSurvivesASaveAndALoad()
    {
        var settings = new AppSettings();
        settings.Developer.DeveloperMode = true;

        var loaded = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, Options), Options)!;

        Assert.True(loaded.Developer.DeveloperMode);
    }
}
