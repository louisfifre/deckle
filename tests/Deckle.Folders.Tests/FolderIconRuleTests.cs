using Deckle.Core;
using Xunit;

namespace Deckle.Folders.Tests;

// The icon the app picks for a folder it adds: its name first, then a
// repository at its top, then the kind of file that makes half of its files.
[Trait("Category", "unit")]
public sealed class FolderIconRuleTests
{
    private static FolderIconEntry F(string name) => new(name, IsFolder: false);
    private static FolderIconEntry D(string name) => new(name, IsFolder: true);

    [Theory]
    [InlineData("Photos", IconSymbol.Photos)]
    [InlineData("Mes documents", IconSymbol.Documents)]
    [InlineData("Vidéos de vacances", IconSymbol.Videos)]
    [InlineData("Téléchargements", IconSymbol.Downloads)]
    [InlineData("Projets", IconSymbol.Code)]
    [InlineData("MUSIQUE", IconSymbol.Music)]
    [InlineData("2019_Archives", IconSymbol.Archive)]
    public void AWordOfTheNameSaysTheIcon(string name, IconSymbol expected)
    {
        Assert.Equal(expected, FolderIconRule.Pick(name, [F("a.pdf")]));
    }

    [Fact]
    public void AWordInsideAnotherWordDoesNotCount()
    {
        // "photographe" holds "photo" but is not the word.
        Assert.Equal(IconSymbol.Folder, FolderIconRule.Pick("Photographe", []));
    }

    [Fact]
    public void ARepositoryAtTheTopIsCodeWhateverItsFiles()
    {
        Assert.Equal(IconSymbol.Code, FolderIconRule.Pick("deckle", [D(".git"), F("README.md"), F("notes.md")]));
        Assert.Equal(IconSymbol.Code, FolderIconRule.Pick("tool", [F("Tool.sln")]));
    }

    [Fact]
    public void TheKindThatMakesHalfOfTheFilesSaysTheIcon()
    {
        Assert.Equal(IconSymbol.Photos,
            FolderIconRule.Pick("Mariage", [F("DSC_0001.JPG"), F("DSC_0002.NEF"), F("liste.txt"), D("Retouches")]));
        Assert.Equal(IconSymbol.Music, FolderIconRule.Pick("Concert", [F("1.flac"), F("2.flac"), F("cover.jpg")]));
    }

    [Fact]
    public void AMixedOrEmptyFolderIsThePlainFolder()
    {
        Assert.Equal(IconSymbol.Folder, FolderIconRule.Pick("D:", [F("a.zip"), F("b.exe"), F("c.jpg"), D("Jeux")]));
        Assert.Equal(IconSymbol.Folder, FolderIconRule.Pick("Vide", []));
        Assert.Equal(IconSymbol.Folder, FolderIconRule.Pick("Sous-dossiers", [D("A"), D("B")]));
    }
}
