using System.Globalization;
using Xunit;

namespace Deckle.Folders.Tests;

// Sizes and dates read as the boards write them.
[Trait("Category", "unit")]
public sealed class EntryFormatTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    [Theory]
    [InlineData(0L, "0 Ko")]
    [InlineData(10L, "1 Ko")]
    [InlineData(6 * 1024L, "6 Ko")]
    [InlineData(412 * 1024L, "412 Ko")]
    [InlineData(2_516_582L, "2,4 Mo")]
    [InlineData(1_395_864_371L, "1,3 Go")]
    public void ASizeReadsInTheUnitTheBoardsUse(long bytes, string expected)
    {
        Assert.Equal(expected, EntryFormat.Size(bytes, French));
    }

    [Fact]
    public void ADateReadsDayMonthYear()
    {
        Assert.Equal("31/12/2023", EntryFormat.Date(new DateTime(2023, 12, 31, 18, 5, 0), French));
    }

    [Theory]
    [InlineData("Contrat-location.pdf", "PDF")]
    [InlineData("Recette-pain.md", "MD")]
    [InlineData("archive.tar.gz", "GZ")]
    [InlineData("Makefile", null)]
    public void ATypeIsNamedByItsExtensionInUpperCase(string name, string? expected)
    {
        Assert.Equal(expected, EntryFormat.TypeExtension(name));
    }

    [Fact]
    public void AFileSummaryReadsTypeThenSizeThenDate()
    {
        string summary = EntryFormat.Summary("Fichier PDF", 412 * 1024L, new DateTime(2023, 12, 31), " · ", French);
        Assert.Equal("Fichier PDF · 412 Ko · 31/12/2023", summary);
    }

    [Fact]
    public void AFolderSummarySkipsTheSizeItDoesNotHave()
    {
        string summary = EntryFormat.Summary("Dossier de fichiers", null, new DateTime(2023, 12, 31), " · ", French);
        Assert.Equal("Dossier de fichiers · 31/12/2023", summary);
    }
}
