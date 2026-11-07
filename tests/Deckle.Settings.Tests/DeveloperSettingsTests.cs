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
    public void ThePlaygroundIsHiddenByDefault()
    {
        Assert.False(new AppSettings().Developer.ShowPlayground);
    }

    [Fact]
    public void AFileWrittenBeforeTheSettingExistedLoadsWithThePlaygroundHidden()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>("""{"appearance":{"theme":"Dark"}}""", Options)!;

        Assert.False(loaded.Developer.ShowPlayground);
    }

    [Fact]
    public void TheSwitchSurvivesASaveAndALoad()
    {
        var settings = new AppSettings();
        settings.Developer.ShowPlayground = true;

        var loaded = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, Options), Options)!;

        Assert.True(loaded.Developer.ShowPlayground);
    }

    [Fact]
    public void DeveloperModeIsOffByDefault()
    {
        Assert.False(new AppSettings().Developer.DeveloperMode);
    }

    [Fact]
    public void AFileWrittenBeforeDeveloperModeExistedLoadsWithItOff()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>(
            """{"developer":{"showPlayground":true}}""", Options)!;

        Assert.False(loaded.Developer.DeveloperMode);
        Assert.True(loaded.Developer.ShowPlayground);
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
