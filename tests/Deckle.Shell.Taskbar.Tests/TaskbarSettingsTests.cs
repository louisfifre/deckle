using System.Text.Json;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The module keeps a handful of values, all off by default. A file written by
// the older, heavier taskbar module carries more keys; they are ignored, and the
// one that still means something is honoured. Read and written through the
// store's own JSON options.
[Trait("Category", "unit")]
public sealed class TaskbarSettingsTests
{
    private static TaskbarSettings Read(string json)
        => JsonSerializer.Deserialize<TaskbarSettings>(json, TaskbarSettingsService.JsonOptions)!;

    [Fact]
    public void NothingIsChangedByDefault()
    {
        foreach (TaskbarSettings settings in new[] { new TaskbarSettings(), Read("{}") })
        {
            Assert.False(settings.ShowAllTrayIcons);
            Assert.Equal(TaskbarBackground.Native, settings.Background);
            Assert.False(settings.CompactTray);
            Assert.False(settings.CompactTaskButtons);
            Assert.False(settings.CompactClock);
            Assert.False(settings.StartMenuFullHeight);
            Assert.False(settings.WindowsKeyShowsTaskbar);
        }
    }

    [Fact]
    public void TheSettingsRoundTrip()
    {
        string json = JsonSerializer.Serialize(new TaskbarSettings
        {
            ShowAllTrayIcons = true,
            Background = TaskbarBackground.Transparent,
            CompactTray = true,
            CompactTaskButtons = true,
            CompactClock = true,
            StartMenuFullHeight = true,
            WindowsKeyShowsTaskbar = true,
        }, TaskbarSettingsService.JsonOptions);

        TaskbarSettings read = Read(json);

        Assert.True(read.ShowAllTrayIcons);
        Assert.Equal(TaskbarBackground.Transparent, read.Background);
        Assert.True(read.CompactTray);
        Assert.True(read.CompactTaskButtons);
        Assert.True(read.CompactClock);
        Assert.True(read.StartMenuFullHeight);
        Assert.True(read.WindowsKeyShowsTaskbar);
    }

    // A person editing the file by hand reads the background by its name.
    [Fact]
    public void TheBackgroundIsWrittenAsItsName()
    {
        string json = JsonSerializer.Serialize(new TaskbarSettings { Background = TaskbarBackground.Black }, TaskbarSettingsService.JsonOptions);

        Assert.Contains("\"background\": \"black\"", json);
        Assert.Equal(TaskbarBackground.Black, Read("""{ "background": "black" }""").Background);
    }

    [Fact]
    public void KeysOfAnOlderFileAreIgnored()
    {
        TaskbarSettings settings = Read("""{ "enabled": true, "showAllTrayIcons": true, "clock": "Deckle" }""");

        Assert.True(settings.ShowAllTrayIcons);
    }
}
