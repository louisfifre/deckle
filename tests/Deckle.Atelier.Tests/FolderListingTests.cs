using Xunit;

namespace Deckle.Atelier.Tests;

// A folder lists as File Explorer lists it by default.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class FolderListingTests
{
    [Fact]
    public void FoldersComeFirstThenFilesEachInNumberAwareNameOrder()
    {
        using var scratch = new ScratchFolder();
        scratch.File("x", "note 10.md");
        scratch.File("x", "note 2.md");
        scratch.Folder("zeta");
        scratch.Folder("Alpha");

        FolderListingResult listing = FolderListing.List(scratch.Path);

        Assert.True(listing.Succeeded);
        Assert.Equal(["Alpha", "zeta", "note 2.md", "note 10.md"], listing.Entries.Select(e => e.Name));
    }

    [Fact]
    public void AFileCarriesItsSizeAndAFolderNone()
    {
        using var scratch = new ScratchFolder();
        scratch.File("12345", "a.txt");
        scratch.Folder("sub");

        FolderListingResult listing = FolderListing.List(scratch.Path);

        Assert.Null(listing.Entries.Single(e => e.Name == "sub").Size);
        Assert.Equal(5, listing.Entries.Single(e => e.Name == "a.txt").Size);
    }

    [Fact]
    public void HiddenItemsAreLeftOut()
    {
        using var scratch = new ScratchFolder();
        string hidden = scratch.File("x", "hidden.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        scratch.File("x", "shown.txt");

        Assert.Equal(["shown.txt"], FolderListing.List(scratch.Path).Entries.Select(e => e.Name));
    }

    [Fact]
    public void AFolderThatIsGoneSaysSo()
    {
        using var scratch = new ScratchFolder();

        FolderListingResult listing = FolderListing.List(Path.Combine(scratch.Path, "gone"));

        Assert.Equal(FolderListingFailure.NotFound, listing.Failure);
        Assert.Empty(listing.Entries);
    }

    [Fact]
    public void TheSampleFolderHoldsFoldersAndFilesOfSeveralSizes()
    {
        using var scratch = new ScratchFolder();

        string root = SampleFolder.Create(scratch.Path);
        FolderListingResult listing = FolderListing.List(root);

        Assert.Contains(listing.Entries, e => e.IsFolder);
        Assert.Contains(listing.Entries, e => e.Size > 1024 * 1024);
        Assert.Contains(listing.Entries, e => e.Size is > 0 and < 1024 * 1024);
        Assert.NotEmpty(FolderListing.Subfolders(Path.Combine(root, "Photos")));
    }
}
