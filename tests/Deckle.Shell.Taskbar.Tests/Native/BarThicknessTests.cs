using System.Text.Json;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The bar's thickness from the setting to the document: the value the page
// keeps, the entry the engine reads, and what the page says when it fails.
[Trait("Category", "unit")]
public sealed class BarThicknessTests
{
    private static readonly ExplorerFunction FrameSize = new(
        "Taskbar.View.dll", TimeDateStamp: 0x6AA3487A, ImageSize: 10055680, Rva: 0x451F0,
        Prologue: Convert.FromHexString("4883EC2885C9743883E901750DF20F10"));

    [Fact]
    public void TheThicknessIsLeftToExplorerUntilTheSwitchIsOn()
    {
        var settings = new TaskbarSettings { BarThickness = 30 };

        Assert.Null(TaskbarCustomization.From(settings, verticalBar: true).BarThickness);

        settings.CustomBarThickness = true;
        TaskbarCustomization wanted = TaskbarCustomization.From(settings, verticalBar: true);
        Assert.Equal(30, wanted.BarThickness);
        Assert.True(wanted.CustomizesExplorer);
    }

    [Fact]
    public void TheThicknessHoldsOnAHorizontalBarToo()
    {
        var settings = new TaskbarSettings { CustomBarThickness = true };

        Assert.Equal(TaskbarCustomization.DefaultBarThickness, TaskbarCustomization.From(settings, verticalBar: false).BarThickness);
    }

    [Theory]
    [InlineData(4, 24)]
    [InlineData(500, 72)]
    public void AThicknessWrittenByHandIsHeldToItsRange(int written, int applied)
    {
        var settings = new TaskbarSettings { CustomBarThickness = true, BarThickness = written };

        Assert.Equal(applied, TaskbarCustomization.From(settings, verticalBar: true).BarThickness);
    }

    [Fact]
    public void TheDocumentAsksTheFunctionToReturnTheThickness()
    {
        var wanted = new TaskbarCustomization { BarThickness = 32 };

        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.Build(wanted, FrameSize));
        JsonElement entry = Assert.Single(document.RootElement.GetProperty("constantReturns").EnumerateArray().ToArray());

        Assert.Equal(TaskbarDocumentBuilder.BarThicknessId, entry.GetProperty("id").GetString());
        Assert.Equal("Taskbar.View.dll", entry.GetProperty("module").GetString());
        Assert.Equal(0x6AA3487Au, entry.GetProperty("timestamp").GetUInt32());
        Assert.Equal(10055680u, entry.GetProperty("imageSize").GetUInt32());
        Assert.Equal(0x451F0u, entry.GetProperty("rva").GetUInt32());
        Assert.Equal("4883EC2885C9743883E901750DF20F10", entry.GetProperty("original").GetString());
        Assert.Equal(32, entry.GetProperty("value").GetDouble());
    }

    [Fact]
    public void WithoutTheFunctionTheDocumentLeavesTheThicknessOut()
    {
        var wanted = new TaskbarCustomization { BarThickness = 32 };

        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.Build(wanted, frameSize: null));

        Assert.False(document.RootElement.TryGetProperty("constantReturns", out _));
    }

    [Fact]
    public void AFunctionFoundForAnotherSettingDoesNotThinTheBar()
    {
        using JsonDocument document = JsonDocument.Parse(TaskbarDocumentBuilder.Build(new TaskbarCustomization(), FrameSize));

        Assert.False(document.RootElement.TryGetProperty("constantReturns", out _));
    }

    [Fact]
    public void AThicknessProblemIsSaidOnlyWhileTheThicknessIsAskedFor()
    {
        var asked = new TaskbarCustomization { BarThickness = 32 };

        Assert.Equal(TaskbarProblem.NotApplied,
            TaskbarProblemReader.Read(asked, null, false, null, false, TaskbarProblem.NotApplied)[TaskbarPart.BarThickness]);
        Assert.Empty(TaskbarProblemReader.Read(new TaskbarCustomization(), null, false, null, false, TaskbarProblem.Error));
    }

    [Fact]
    public void AnExplorerEngineThatCannotStartPutsTheThicknessInError()
    {
        var asked = new TaskbarCustomization { BarThickness = 32 };

        Assert.Equal(TaskbarProblem.Error,
            TaskbarProblemReader.Read(asked, null, explorerUnavailable: true, null, false, TaskbarProblem.NotApplied)[TaskbarPart.BarThickness]);
    }
}
