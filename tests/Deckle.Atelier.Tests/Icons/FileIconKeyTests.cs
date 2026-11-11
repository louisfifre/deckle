using Xunit;

namespace Deckle.Atelier.Tests;

// Which rows share one icon, and which pictures are told apart.
[Trait("Category", "unit")]
public sealed class FileIconKeyTests
{
    [Fact]
    public void FilesOfOneTypeShareTheirIcon()
    {
        Assert.Equal(
            FileIconKey.For(@"C:\Notes\a.md", isFolder: false, 20),
            FileIconKey.For(@"D:\Other\B.MD", isFolder: false, 20));
    }

    [Fact]
    public void TwoTypesAreToldApart()
    {
        Assert.NotEqual(
            FileIconKey.For(@"C:\Notes\a.md", isFolder: false, 20),
            FileIconKey.For(@"C:\Notes\a.pdf", isFolder: false, 20));
    }

    [Fact]
    public void OneTypeAtTwoSizesIsTwoIcons()
    {
        Assert.NotEqual(
            FileIconKey.For(@"C:\Notes\a.md", isFolder: false, 16),
            FileIconKey.For(@"C:\Notes\a.md", isFolder: false, 24));
    }

    [Theory]
    [InlineData(@"C:\Apps\one.exe", @"C:\Apps\two.exe")]
    [InlineData(@"C:\Links\one.lnk", @"C:\Links\two.lnk")]
    [InlineData(@"C:\Art\one.ico", @"C:\Art\two.ico")]
    [InlineData(@"C:\Links\one.url", @"C:\Links\two.url")]
    public void ATypeThatDrawsAnIconPerFileIsKeyedByFile(string one, string two)
    {
        FileIconKey first = FileIconKey.For(one, isFolder: false, 20);
        Assert.True(first.PerFile);
        Assert.NotEqual(first, FileIconKey.For(two, isFolder: false, 20));
        Assert.Equal(first, FileIconKey.For(one.ToUpperInvariant(), isFolder: false, 20));
    }

    [Fact]
    public void AFolderIsKeyedByItsPathWhateverItsName()
    {
        // A folder named like a file type is still a folder, and can carry a
        // custom icon of its own (desktop.ini).
        FileIconKey folder = FileIconKey.For(@"C:\Notes\Archive.zip", isFolder: true, 20);
        Assert.True(folder.PerFile);
        Assert.NotEqual(folder, FileIconKey.For(@"C:\Notes\Other.zip", isFolder: true, 20));
        Assert.NotEqual(folder, FileIconKey.For(@"C:\Notes\Archive.zip", isFolder: false, 20));
    }

    [Fact]
    public void AFileWithoutExtensionSharesTheBlankTypeIcon()
    {
        Assert.Equal(
            FileIconKey.For(@"C:\Notes\README", isFolder: false, 20),
            FileIconKey.For(@"C:\Notes\LICENSE", isFolder: false, 20));
    }

    [Fact]
    public void AThumbnailIsKeyedByFileSizeAndLastWrite()
    {
        var written = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        FileThumbnailKey key = FileThumbnailKey.For(@"C:\Photos\a.jpg", 256, written);

        Assert.Equal(key, FileThumbnailKey.For(@"C:\PHOTOS\A.JPG", 256, written));
        Assert.NotEqual(key, FileThumbnailKey.For(@"C:\Photos\b.jpg", 256, written));
        Assert.NotEqual(key, FileThumbnailKey.For(@"C:\Photos\a.jpg", 128, written));
        Assert.NotEqual(key, FileThumbnailKey.For(@"C:\Photos\a.jpg", 256, written.AddSeconds(1)));
    }
}
