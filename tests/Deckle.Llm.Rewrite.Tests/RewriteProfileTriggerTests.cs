using System.Text.Json;
using Deckle.Catalog;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// What starts a profile — its length threshold and its shortcut — survives
// the settings file, and a stored threshold lands on a slider stop.
[Trait("Category", "unit")]
public sealed class RewriteProfileTriggerTests
{
    // The options LlmSettingsService writes the file with.
    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ThresholdAndShortcutRoundTripThroughTheSettingsFile()
    {
        var settings = new LlmSettings();
        settings.Profiles[0].AutoSelect = true;
        settings.Profiles[0].AutoSelectFromMinutes = 15;
        settings.Profiles[0].Hotkey = new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Win, 0x29);

        LlmSettings read = JsonSerializer.Deserialize<LlmSettings>(
            JsonSerializer.Serialize(settings, FileOptions), FileOptions)!;

        Assert.True(read.Profiles[0].AutoSelect);
        Assert.Equal(15, read.Profiles[0].AutoSelectFromMinutes);
        Assert.Equal(settings.Profiles[0].Hotkey, read.Profiles[0].Hotkey);
        Assert.False(read.Profiles[1].AutoSelect);
        Assert.Null(read.Profiles[1].Hotkey);
    }

    [Fact]
    public void AFileWrittenBeforeTheThresholdsLeavesEveryProfileOff()
    {
        LlmSettings read = JsonSerializer.Deserialize<LlmSettings>(
            """{ "profiles": [ { "id": "a", "name": "Old" } ] }""", FileOptions)!;

        Assert.False(read.Profiles[0].AutoSelect);
        Assert.Null(read.Profiles[0].Hotkey);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(12, 10)]
    [InlineData(13, 15)]
    [InlineData(45, 40)]
    [InlineData(500, 60)]
    [InlineData(0, 1)]
    public void AStoredThresholdLandsOnTheNearestStop(int stored, int expected)
    {
        Assert.Equal(
            expected,
            RewriteProfile.AutoSelectStopsMinutes[ProfileViewModel.NearestStopIndex(stored)]);
    }
}
