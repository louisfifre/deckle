using Xunit;

namespace Deckle.Atelier.Tests;

[Trait("Category", "unit")]
public sealed class PhotoFormatsTests
{
    [Theory]
    [InlineData("DSC_0217.NEF", true, true)]
    [InlineData("IMG_0001.CR3", true, true)]
    [InlineData("IMG_0001.cr2", true, true)]
    [InlineData("P1000001.RW2", true, true)]
    [InlineData("DSCF0001.RAF", true, true)]
    [InlineData("IMG_0001.DNG", true, true)]
    [InlineData("DSC_0217.JPG", true, false)]
    [InlineData("IMG_0001.HEIC", true, false)]
    [InlineData("scan.tiff", true, false)]
    [InlineData("capture.png", false, false)]
    [InlineData("DSC_0217.NEF.xmp", false, false)]
    [InlineData("notes.txt", false, false)]
    [InlineData("noextension", false, false)]
    public void APhotoIsKnownByItsExtension(string name, bool photo, bool raw)
    {
        Assert.Equal(photo, PhotoFormats.IsPhoto(name));
        Assert.Equal(raw, PhotoFormats.IsRaw(name));
    }
}

// The photo tool lists the photos of the folder it is in, nothing else.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class PhotoFolderTests
{
    [Fact]
    public void OnlyThePhotosOfTheFolderItselfAreListed()
    {
        using var scratch = new ScratchFolder();
        string shoot = scratch.Folder("Shoot");
        scratch.File("x", "Shoot", "DSC_0002.NEF");
        scratch.File("x", "Shoot", "DSC_0002.NEF.xmp");
        scratch.File("x", "Shoot", "DSC_0010.JPG");
        scratch.File("x", "Shoot", "notes.txt");
        scratch.File("x", "Shoot", "Exports", "DSC_0001.jpg");

        PhotoFolderResult result = PhotoFolder.List(shoot);

        Assert.True(result.Succeeded);
        Assert.Equal(["DSC_0002.NEF", "DSC_0010.JPG"], result.Photos.Select(p => p.Name));
        Assert.True(result.Photos[0].IsRaw);
        Assert.False(result.Photos[1].IsRaw);
    }

    [Fact]
    public void AFolderThatIsGoneSaysSo()
    {
        using var scratch = new ScratchFolder();

        PhotoFolderResult result = PhotoFolder.List(Path.Combine(scratch.Path, "gone"));

        Assert.Equal(FolderListingFailure.NotFound, result.Failure);
        Assert.Empty(result.Photos);
    }
}
