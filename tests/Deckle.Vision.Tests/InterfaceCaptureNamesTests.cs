using System.Text.Json;
using Xunit;

namespace Deckle.Vision.Tests;

// The file names and the index are the contract with whoever reads a capture
// folder: the same page must land under the same name from one capture to the
// next, and the index must say what each file shows.
[Trait("Category", "unit")]
public sealed class InterfaceCaptureNamesTests
{
    [Theory]
    [InlineData("Deckle.Transcription.RecordingPage, Deckle.Transcription", "recording")]
    [InlineData("Deckle.Settings.GeneralPage", "general")]
    [InlineData("PrecisionScrollPage", "precision-scroll")]
    [InlineData("Deckle.Llm.Rewrite.LLMRewritePage, Deckle.Llm.Rewrite", "llm-rewrite")]
    [InlineData("Page", "page")]
    public void PageSlugComesFromThePageTypeName(string pageTag, string expected)
        => Assert.Equal(expected, InterfaceCaptureNames.PageSlug(pageTag));

    [Theory]
    [InlineData("Deckle Settings", "deckle-settings")]
    [InlineData("Journal d'activité", "journal-d-activite")]
    [InlineData("  ", "window")]
    public void SlugIsLowercaseAsciiWordsJoinedByHyphens(string text, string expected)
        => Assert.Equal(expected, InterfaceCaptureNames.Slug(text));

    [Fact]
    public void PageFileNameIsTheWindowThenThePage()
        => Assert.Equal("settings-recording", InterfaceCaptureNames.BaseName("settings", "recording"));

    [Fact]
    public void FolderNameSortsAsText()
        => Assert.Equal("2026-11-07_22-10-03",
            InterfaceCaptureNames.FolderName(new DateTimeOffset(2026, 11, 7, 22, 10, 3, TimeSpan.FromHours(1))));

    [Fact]
    public void ASecondCaptureUnderOneNameGetsANumberAndNeverOverwrites()
    {
        var taken = new HashSet<string>();

        Assert.Equal("hud", InterfaceCaptureNames.Unique("hud", taken));
        Assert.Equal("hud-2", InterfaceCaptureNames.Unique("hud", taken));
        Assert.Equal("hud-3", InterfaceCaptureNames.Unique("hud", taken));
    }

    [Fact]
    public void IndexListsEachImageWithItsWindowPageTitleAndSize()
    {
        var index = new InterfaceCaptureIndex(new DateTimeOffset(2026, 11, 7, 22, 10, 3, TimeSpan.FromHours(1)));
        index.Add(new InterfaceCaptureEntry("settings-recording.png", "settings", "Enregistrement", "Paramètres de Deckle", 1920, 2880));
        index.Add(new InterfaceCaptureEntry("log.png", "log", null, "Journal", 1200, 800));
        index.Add(new InterfaceCaptureFailure("hud", "first_frame", "TimeoutException"));

        using JsonDocument json = JsonDocument.Parse(index.ToJson());
        JsonElement root = json.RootElement;

        Assert.Equal("2026-11-07T22:10:03+01:00", root.GetProperty("capturedAt").GetString());
        Assert.Equal(InterfaceCaptureIndex.Technique, root.GetProperty("technique").GetString());

        JsonElement page = root.GetProperty("captures")[0];
        Assert.Equal("settings-recording.png", page.GetProperty("file").GetString());
        Assert.Equal("settings", page.GetProperty("window").GetString());
        Assert.Equal("Enregistrement", page.GetProperty("page").GetString());
        Assert.Equal("Paramètres de Deckle", page.GetProperty("title").GetString());
        Assert.Equal(1920, page.GetProperty("width").GetInt32());
        Assert.Equal(2880, page.GetProperty("height").GetInt32());

        // A window captured whole has no page.
        Assert.False(root.GetProperty("captures")[1].TryGetProperty("page", out _));

        JsonElement failure = root.GetProperty("failures")[0];
        Assert.Equal("hud", failure.GetProperty("name").GetString());
        Assert.Equal("first_frame", failure.GetProperty("step").GetString());
    }

    [Fact]
    public void IndexKeepsAccentedLettersReadable()
    {
        var index = new InterfaceCaptureIndex(DateTimeOffset.UnixEpoch);
        index.Add(new InterfaceCaptureEntry("log.png", "log", null, "Journal d'activité", 1, 1));

        Assert.Contains("activité", index.ToJson());
    }
}
