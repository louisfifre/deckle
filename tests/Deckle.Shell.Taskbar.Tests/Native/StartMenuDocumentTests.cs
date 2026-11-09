using System.Text.Json;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class StartMenuDocumentTests
{
    [Fact]
    public void AFullHeightStartMenuBuildsTheHeightBehaviorAndNoRule()
    {
        var customization = new TaskbarCustomization { StartMenu = new StartMenuCustomization(FullHeight: true) };

        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.BuildStartMenu(customization));

        Assert.Equal(0, document.RootElement.GetProperty("rules").GetArrayLength());
        JsonElement behavior = document.RootElement.GetProperty("behaviors").GetProperty("startMenuHeight");
        Assert.True(behavior.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void ANativeStartMenuBuildsAnEmptyDocumentThatRestoresEverything()
    {
        using JsonDocument none = JsonDocument.Parse(TaskbarDocumentBuilder.BuildStartMenu(new TaskbarCustomization()));
        using JsonDocument off = JsonDocument.Parse(TaskbarDocumentBuilder.BuildStartMenu(
            new TaskbarCustomization { StartMenu = new StartMenuCustomization(FullHeight: false) }));

        foreach (JsonDocument document in new[] { none, off })
        {
            Assert.Equal(0, document.RootElement.GetProperty("rules").GetArrayLength());
            Assert.Empty(document.RootElement.GetProperty("behaviors").EnumerateObject());
        }
    }

    [Fact]
    public void TheStartMenuSettingNeverReachesTheTaskbarDocument()
    {
        var customization = new TaskbarCustomization
        {
            Background = TaskbarBackground.Transparent,
            StartMenu = new StartMenuCustomization(FullHeight: true),
        };

        string taskbar = TaskbarDocumentBuilder.Build(customization);

        Assert.DoesNotContain("startMenuHeight", taskbar);
        Assert.Equal(TaskbarDocumentBuilder.Build(customization with { StartMenu = null }), taskbar);
    }

    [Fact]
    public void TheStartMenuDocumentCarriesNoTaskbarRule()
    {
        var customization = new TaskbarCustomization
        {
            Background = TaskbarBackground.Transparent,
            TrayIconPitch = 24,
            StartMenu = new StartMenuCustomization(FullHeight: true),
        };

        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.BuildStartMenu(customization));

        Assert.Equal(0, document.RootElement.GetProperty("rules").GetArrayLength());
    }

    [Fact]
    public void ExplorerKeepsTheBarePipeNameAndTheStartMenuHostGetsItsOwn()
    {
        Assert.Equal("Deckle.Taskbar.3", TaskbarTarget.Explorer.PipeName(3));
        Assert.Equal("Deckle.Taskbar.3.StartMenuExperienceHost", TaskbarTarget.StartMenu.PipeName(3));
    }

    [Fact]
    public void OnlyTheStartMenuHostIsPackaged()
    {
        Assert.False(TaskbarTarget.Explorer.Packaged);
        Assert.True(TaskbarTarget.StartMenu.Packaged);
    }
}
