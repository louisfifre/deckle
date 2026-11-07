using System.Text.Json;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The module keeps one value. A file written by the older, heavier taskbar
// module carries more keys; they are ignored, and the one that still means
// something is honoured. Read and written through the store's own JSON options.
[Trait("Category", "unit")]
public sealed class TaskbarSettingsTests
{
    private static TaskbarSettings Read(string json)
        => JsonSerializer.Deserialize<TaskbarSettings>(json, TaskbarSettingsService.JsonOptions)!;

    [Fact]
    public void TheTrayIconsAreNotPromotedByDefault()
    {
        Assert.False(new TaskbarSettings().ShowAllTrayIcons);
        Assert.False(Read("{}").ShowAllTrayIcons);
    }

    [Fact]
    public void TheSettingRoundTrips()
    {
        string json = JsonSerializer.Serialize(new TaskbarSettings { ShowAllTrayIcons = true }, TaskbarSettingsService.JsonOptions);

        Assert.Contains("\"showAllTrayIcons\": true", json);
        Assert.True(Read(json).ShowAllTrayIcons);
    }

    [Fact]
    public void KeysOfAnOlderFileAreIgnored()
    {
        TaskbarSettings settings = Read("""{ "enabled": true, "showAllTrayIcons": true, "hideRecommended": true, "clock": "Deckle" }""");

        Assert.True(settings.ShowAllTrayIcons);
    }
}