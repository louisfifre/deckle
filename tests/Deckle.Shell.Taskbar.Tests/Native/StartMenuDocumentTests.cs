using System.Text.Json;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class StartMenuDocumentTests
{
    [Fact]
    public void AFullHeightStartMenuBuildsTheHeightBehaviorAndNoRule()
    {
        var customization = new TaskbarCustomization { StartMenu = new StartMenuCustomization { FullHeight = true } };

        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.BuildStartMenu(customization));

        Assert.Equal(0, document.RootElement.GetProperty("rules").GetArrayLength());
        JsonElement behavior = document.RootElement.GetProperty("behaviors").GetProperty("startMenuHeight");
        Assert.True(behavior.GetProperty("enabled").GetBoolean());
    }

    // The width is a behavior: only the engine can measure the screen it holds
    // the value to. A filled width carries no number.
    [Fact]
    public void AStartMenuWidthBuildsTheWidthBehavior()
    {
        var value = new TaskbarCustomization { StartMenu = new StartMenuCustomization { Width = StartMenuWidthRequest.Of(640) } };
        var fill = new TaskbarCustomization { StartMenu = new StartMenuCustomization { Width = StartMenuWidthRequest.Fill } };

        using JsonDocument valueDocument = JsonDocument.Parse(TaskbarDocumentBuilder.BuildStartMenu(value));
        using JsonDocument fillDocument = JsonDocument.Parse(TaskbarDocumentBuilder.BuildStartMenu(fill));

        JsonElement byValue = valueDocument.RootElement.GetProperty("behaviors").GetProperty("startMenuWidth");
        Assert.True(byValue.GetProperty("enabled").GetBoolean());
        Assert.False(byValue.GetProperty("fill").GetBoolean());
        Assert.Equal(640, byValue.GetProperty("width").GetInt32());
        Assert.False(valueDocument.RootElement.GetProperty("behaviors").TryGetProperty("startMenuHeight", out _));

        JsonElement filled = fillDocument.RootElement.GetProperty("behaviors").GetProperty("startMenuWidth");
        Assert.True(filled.GetProperty("fill").GetBoolean());
        Assert.False(filled.TryGetProperty("width", out _));
    }

    [Fact]
    public void ANativeStartMenuBuildsAnEmptyDocumentThatRestoresEverything()
    {
        using JsonDocument none = JsonDocument.Parse(TaskbarDocumentBuilder.BuildStartMenu(new TaskbarCustomization()));
        using JsonDocument off = JsonDocument.Parse(TaskbarDocumentBuilder.BuildStartMenu(
            new TaskbarCustomization { StartMenu = new StartMenuCustomization { FullHeight = false } }));

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
            StartMenu = new StartMenuCustomization { FullHeight = true },
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
            StartMenu = new StartMenuCustomization { FullHeight = true },
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
    public void ASideInstanceGetsItsNameAfterTheTargetsPipe()
    {
        Assert.Equal("Deckle.Taskbar.3.harness", TaskbarTarget.Explorer.PipeName(3, "harness"));
        Assert.Equal("Deckle.Taskbar.3.StartMenuExperienceHost.harness", TaskbarTarget.StartMenu.PipeName(3, "harness"));
        Assert.Equal("Deckle.Taskbar.3", TaskbarTarget.Explorer.PipeName(3, ""));
    }

    [Fact]
    public void OnlyTheStartMenuHostIsPackaged()
    {
        Assert.False(TaskbarTarget.Explorer.Packaged);
        Assert.True(TaskbarTarget.StartMenu.Packaged);
    }
}
