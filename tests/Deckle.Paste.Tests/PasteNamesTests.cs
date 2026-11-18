using Xunit;

namespace Deckle.Paste.Tests;

// The names a paste is written under: its moment for a text or an image, and
// the Explorer copy numbering for a name already taken.
[Trait("Category", "unit")]
public sealed class PasteNamesTests
{
    [Fact]
    public void AGeneratedNameReadsTheMomentInItsOwnOffset()
    {
        var moment = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(-7));

        Assert.Equal("paste-2026-01-02-030405.png", PasteNames.Generated(moment, ".png"));
    }

    [Fact]
    public void CandidatesNumberBeforeTheExtensionFromTwo()
    {
        string[] names = [.. PasteNames.Candidates("C:\\out", "report.final.txt").Take(3).Select(Path.GetFileName)!];

        Assert.Equal(["report.final.txt", "report.final (2).txt", "report.final (3).txt"], names);
    }

    [Fact]
    public void ANameWithoutExtensionIsNumberedAtItsEnd()
    {
        string[] names = [.. PasteNames.Candidates("C:\\out", "Album").Take(2).Select(Path.GetFileName)!];

        Assert.Equal(["Album", "Album (2)"], names);
    }
}
