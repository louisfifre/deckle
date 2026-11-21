using System.Globalization;
using Xunit;

namespace Deckle.Folders.Tests;

// Several items selected read in the panel as File Explorer's details pane
// reads them: how many, of which kinds, and the size of the files alone.
[Trait("Category", "unit")]
public sealed class SelectionSummaryTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly DateTime Date = new(2026, 11, 5);

    private static readonly SelectionTexts Texts = new(
        "{0} élément sélectionné", "{0} éléments sélectionnés",
        "{0} dossier", "{0} dossiers",
        "{0} fichier", "{0} fichiers",
        " · ",
        "{0}, sans les dossiers");

    private static FolderEntry File(string name, long bytes) => new(name, @"C:\x\" + name, false, bytes, Date);

    private static FolderEntry Folder(string name) => new(name, @"C:\x\" + name, true, null, Date);

    [Fact]
    public void TheSizeIsTheSumOfTheFilesAndFoldersAreCountedNotMeasured()
    {
        SelectionSummary summary = SelectionSummary.Of([File("a.pdf", 1024), Folder("Notes"), File("b.zip", 3 * 1024)]);

        Assert.Equal(3, summary.Items);
        Assert.Equal(1, summary.Folders);
        Assert.Equal(2, summary.Files);
        Assert.Equal(4 * 1024, summary.FileBytes);
    }

    [Fact]
    public void FilesAloneReadTheirCountTheirKindAndTheirSize()
    {
        SelectionWording wording = SelectionSummary.Of([File("a.pdf", 412 * 1024), File("b.zip", 3_200_000)]).Describe(Texts, French);

        Assert.Equal("2 éléments sélectionnés", wording.Title);
        Assert.Equal("2 fichiers", wording.Kinds);
        Assert.Equal("3,5\u00A0Mo", wording.Size);
    }

    [Fact]
    public void FoldersAmongTheFilesAreSaidToBeLeftOutOfTheSize()
    {
        SelectionWording wording = SelectionSummary.Of([Folder("Notes"), File("a.pdf", 6 * 1024), File("b.md", 10)]).Describe(Texts, French);

        Assert.Equal("3 éléments sélectionnés", wording.Title);
        Assert.Equal("1 dossier · 2 fichiers", wording.Kinds);
        Assert.Equal("7\u00A0Ko, sans les dossiers", wording.Size);
    }

    [Fact]
    public void FoldersAloneHaveNoSize()
    {
        SelectionWording wording = SelectionSummary.Of([Folder("Notes"), Folder("Photos")]).Describe(Texts, French);

        Assert.Equal("2 dossiers", wording.Kinds);
        Assert.Null(wording.Size);
    }

    [Fact]
    public void EmptyFilesStillCountAndWeighNothing()
    {
        SelectionWording wording = SelectionSummary.Of([File("a.txt", 0), File("b.txt", 0)]).Describe(Texts, French);

        Assert.Equal("0\u00A0Ko", wording.Size);
    }

    [Theory]
    [InlineData(0, "fr-FR", "0 fichier")]
    [InlineData(1, "fr-FR", "1 fichier")]
    [InlineData(2, "fr-FR", "2 fichiers")]
    [InlineData(0, "en-US", "0 fichiers")]
    [InlineData(1, "en-US", "1 fichier")]
    public void ACountTakesTheSingularItsCultureGivesIt(int count, string culture, string expected)
    {
        Assert.Equal(expected, EntryFormat.Count(count, "{0} fichier", "{0} fichiers", CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void ALargeCountTakesItsGroupSeparator()
    {
        string text = EntryFormat.Count(1234, "{0} fichier", "{0} fichiers", French);

        Assert.Equal("1" + French.NumberFormat.NumberGroupSeparator + "234 fichiers", text);
    }
}
