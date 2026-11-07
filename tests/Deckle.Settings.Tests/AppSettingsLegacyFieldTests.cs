using System.Text.Json;
using Xunit;

namespace Deckle.Settings.Tests;

// Overlay.Animations left the settings model: HUD animations follow the Windows
// animation preference. Files written by earlier builds still carry the field.
[Trait("Category", "regression")]
public sealed class AppSettingsLegacyFieldTests
{
    // Same options SettingsService hands to its store.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ASettingsFileStillCarryingTheRemovedAnimationsFieldLoadsAndKeepsTheOtherValues()
    {
        const string json = """{"overlay":{"enabled":false,"animations":false,"position":"TopCenter"}}""";

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);

        Assert.NotNull(settings);
        Assert.False(settings.Overlay.Enabled);
        Assert.Equal("TopCenter", settings.Overlay.Position);
    }

    [Fact]
    public void TheModelNoLongerDeclaresAnAnimationsSwitch()
    {
        Assert.Null(typeof(OverlaySettings).GetProperty("Animations"));
    }
}
