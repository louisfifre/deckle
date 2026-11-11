using System.Text.Json;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The larger thumbnails from the setting to the document: the box the page
// keeps, the entry the engine reads, and what the page says when it fails.
[Trait("Category", "unit")]
public sealed class ThumbnailBoxTests
{
    // Taskbar.View.dll 2608.26001.200.0 on build 26100, as Microsoft's symbols place it.
    private static readonly ExplorerFunction ThumbnailSize = new(
        "Taskbar.View.dll", TimeDateStamp: 0x6AA3487A, ImageSize: 10055680, Rva: 0x153030,
        Prologue: Convert.FromHexString("4883EC18F30F102DB85E6D00F30F101D"));

    [Fact]
    public void TheThumbnailsAreLeftToExplorerUntilTheSwitchIsOn()
    {
        var settings = new TaskbarSettings { ThumbnailMaxWidth = 500, ThumbnailMaxHeight = 300 };

        Assert.Null(TaskbarCustomization.From(settings, verticalBar: true).Thumbnails);

        settings.LargerThumbnails = true;
        TaskbarCustomization wanted = TaskbarCustomization.From(settings, verticalBar: false);
        Assert.Equal(new ThumbnailBox(500, 300), wanted.Thumbnails);
        Assert.True(wanted.CustomizesExplorer);
    }

    [Fact]
    public void TheDocumentLeadsTheFunctionToTheBox()
    {
        var wanted = new TaskbarCustomization { Thumbnails = new ThumbnailBox(640, 330) };

        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.Build(wanted, thumbnailSize: ThumbnailSize));
        JsonElement entry = document.RootElement.GetProperty("thumbnailBox");

        Assert.Equal(TaskbarDocumentBuilder.ThumbnailBoxId, entry.GetProperty("id").GetString());
        Assert.Equal("Taskbar.View.dll", entry.GetProperty("module").GetString());
        Assert.Equal(0x6AA3487Au, entry.GetProperty("timestamp").GetUInt32());
        Assert.Equal(10055680u, entry.GetProperty("imageSize").GetUInt32());
        Assert.Equal(0x153030u, entry.GetProperty("rva").GetUInt32());
        Assert.Equal("4883EC18F30F102DB85E6D00F30F101D", entry.GetProperty("original").GetString());
        Assert.Equal(640, entry.GetProperty("maxWidth").GetDouble());
        Assert.Equal(330, entry.GetProperty("maxHeight").GetDouble());
    }

    [Fact]
    public void WithoutTheFunctionTheDocumentLeavesTheThumbnailsOut()
    {
        var wanted = new TaskbarCustomization { Thumbnails = new ThumbnailBox(640, 330) };

        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.Build(wanted, thumbnailSize: null));

        Assert.False(document.RootElement.TryGetProperty("thumbnailBox", out _));
    }

    [Fact]
    public void AFunctionFoundForAnotherSettingDoesNotGrowTheThumbnails()
    {
        var wanted = new TaskbarCustomization { BarThickness = 32 };

        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.Build(wanted, ThumbnailSize, ThumbnailSize));

        Assert.False(document.RootElement.TryGetProperty("thumbnailBox", out _));
    }

    [Fact]
    public void AThumbnailProblemIsSaidOnlyWhileTheThumbnailsAreAskedFor()
    {
        var asked = new TaskbarCustomization { Thumbnails = new ThumbnailBox(640, 330) };

        Assert.Equal(TaskbarProblem.NotApplied,
            TaskbarProblemReader.Read(asked, null, false, null, false, thumbnails: TaskbarProblem.NotApplied)[TaskbarPart.Thumbnails]);
        Assert.Empty(TaskbarProblemReader.Read(new TaskbarCustomization(), null, false, null, false, thumbnails: TaskbarProblem.Error));
    }

    [Fact]
    public void AThicknessProblemSaysNothingOfTheThumbnails()
    {
        var asked = new TaskbarCustomization { Thumbnails = new ThumbnailBox(640, 330) };

        Assert.Empty(TaskbarProblemReader.Read(asked, null, false, null, false, barThickness: TaskbarProblem.Error));
    }
}
