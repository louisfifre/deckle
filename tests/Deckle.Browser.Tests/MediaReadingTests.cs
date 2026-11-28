using Xunit;

namespace Deckle.Browser.Tests;

[Trait("Category", "unit")]
public sealed class MediaReadingTests
{
    // What ExecuteScriptAsync returns for a script that returns JSON.stringify(...): the JSON text of a string.
    private static string AsScriptResult(string json) => System.Text.Json.JsonSerializer.Serialize(json);

    [Fact]
    public void AReadAsAScriptReturnsItParses()
    {
        string result = AsScriptResult(
            """{"title":"Walk tone","artist":"Deckle","position":12.5,"duration":200,"paused":false,"hasVideo":true,"hasNext":true}""");

        MediaReading? reading = MediaReading.Parse(result);

        Assert.Equal(new MediaReading("Walk tone", "Deckle", 12.5, 200, false, true, true), reading);
        Assert.Equal(0.0625, reading!.Progress);
    }

    [Fact]
    public void AMissingFieldTakesItsQuietDefault()
    {
        MediaReading? reading = MediaReading.Parse("""{"title":"T"}""");

        Assert.Equal(new MediaReading("T", "", 0, 0, true, false, false), reading);
        Assert.Equal(0, reading!.Progress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"not json\"")]
    [InlineData("{ broken")]
    public void NothingToReadGivesNull(string? result) => Assert.Null(MediaReading.Parse(result));

    [Fact]
    public void AStreamWithNoDurationAndABadNumberHaveNoProgress()
    {
        MediaReading? reading = MediaReading.Parse("""{"position":30,"duration":-1}""");
        Assert.Equal(0, reading!.Duration);
        Assert.Equal(0, reading.Progress);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5.9, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-3, "0:00")]
    [InlineData(double.NaN, "0:00")]
    public void ATimeIsShownAsMinutesAndSeconds(double seconds, string expected) =>
        Assert.Equal(expected, MediaReading.FormatTime(seconds));
}
