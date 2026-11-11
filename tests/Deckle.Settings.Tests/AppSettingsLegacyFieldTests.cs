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

    // Dictation no longer pastes: the text always lands on the clipboard, so the
    // whole "paste" section left the model. A file written by an earlier build
    // still carries it, and must load with the other sections intact.
    [Fact]
    public void ASettingsFileStillCarryingTheRemovedPasteSectionLoadsAndKeepsTheOtherValues()
    {
        const string json = """{"paste":{"autoPasteEnabled":true},"overlay":{"enabled":false,"position":"TopCenter"}}""";

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);

        Assert.NotNull(settings);
        Assert.False(settings.Overlay.Enabled);
        Assert.Equal("TopCenter", settings.Overlay.Position);
    }

    [Fact]
    public void TheModelNoLongerDeclaresAPasteSection()
    {
        Assert.Null(typeof(AppSettings).GetProperty("Paste"));
    }
}
