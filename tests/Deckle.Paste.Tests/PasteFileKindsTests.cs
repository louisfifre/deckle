using Xunit;

namespace Deckle.Paste.Tests;

// The icon a saved file's row shows, read from its name.
[Trait("Category", "unit")]
public sealed class PasteFileKindsTests
{
    [Theory]
    [InlineData("photo.JPG", PasteFileKind.Image)]
    [InlineData("clip.webm", PasteFileKind.Video)]
    [InlineData("take.flac", PasteFileKind.Audio)]
    [InlineData("report.pdf", PasteFileKind.Document)]
    [InlineData("backup.dat", PasteFileKind.Other)]
    [InlineData("README", PasteFileKind.Other)]
    public void AFileIsKnownByItsExtension(string name, PasteFileKind kind)
    {
        Assert.Equal(kind, PasteFileKinds.Of(name, isFolder: false));
    }

    [Fact]
    public void AFolderIsAFolderWhateverItsName()
    {
        Assert.Equal(PasteFileKind.Folder, PasteFileKinds.Of("Holiday.mp4", isFolder: true));
    }
}
