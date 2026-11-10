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
            Assert.False(settings.HideStartMenuRecommended);
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
            TrayIconPitch = 30,
            CompactTaskButtons = true,
            TaskButtonPitch = 36,
            CompactClock = true,
            StartMenuFullHeight = true,
            WindowsKeyShowsTaskbar = true,
        }, TaskbarSettingsService.JsonOptions);

        TaskbarSettings read = Read(json);

        Assert.True(read.ShowAllTrayIcons);
        Assert.Equal(TaskbarBackground.Transparent, read.Background);
        Assert.True(read.CompactTray);
        Assert.Equal(30, read.TrayIconPitch);
        Assert.True(read.CompactTaskButtons);
        Assert.Equal(36, read.TaskButtonPitch);
        Assert.True(read.CompactClock);
        Assert.True(read.StartMenuFullHeight);
        Assert.True(read.WindowsKeyShowsTaskbar);
    }

    [Fact]
    public void TheStartMenuWidthIsWrittenAsItsNameWithItsValue()
    {
        string json = JsonSerializer.Serialize(new TaskbarSettings { StartMenuWidth = StartMenuWidth.Custom, StartMenuCustomWidth = 580 }, TaskbarSettingsService.JsonOptions);
        TaskbarSettings read = Read(json);

        Assert.Contains("\"startMenuWidth\": \"custom\"", json);
        Assert.Equal(StartMenuWidth.Custom, read.StartMenuWidth);
        Assert.Equal(580, read.StartMenuCustomWidth);
        Assert.Equal(StartMenuWidth.Native, Read("{}").StartMenuWidth);
    }

    // A person editing the file by hand reads the background by its name.
    [Fact]
    public void TheBackgroundIsWrittenAsItsName()
    {
        string json = JsonSerializer.Serialize(new TaskbarSettings { Background = TaskbarBackground.Black }, TaskbarSettingsService.JsonOptions);

        Assert.Contains("\"background\": \"black\"", json);
        Assert.Equal(TaskbarBackground.Black, Read("""{ "background": "black" }""").Background);
    }

    // A file written before the pitches could be set (v0.104) has the switches
    // and no pitch: the compact cards keep the 24 px they always had.
    [Fact]
    public void AFileFromBeforeThePitchesKeepsTheCompactPitch()
    {
        TaskbarSettings settings = Read("""{ "compactTray": true, "compactTaskButtons": true }""");

        Assert.True(settings.CompactTray);
        Assert.True(settings.CompactTaskButtons);
        Assert.Equal(24, settings.TrayIconPitch);
        Assert.Equal(24, settings.TaskButtonPitch);
    }

    [Fact]
    public void KeysOfAnOlderFileAreIgnored()
    {
        TaskbarSettings settings = Read("""{ "enabled": true, "showAllTrayIcons": true, "clock": "Deckle" }""");

        Assert.True(settings.ShowAllTrayIcons);
    }
}
