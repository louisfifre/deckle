using Xunit;

namespace Deckle.Folders.Tests;

// A folder lists as File Explorer lists it by default.
[Collection("folders provider singleton")]
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

    // A folder Windows refuses to list says so: it does not pass for an empty
    // one, which would tell the person there is nothing in it.
    [Fact]
    [Trait("Category", "regression")]
    public void AFolderWindowsRefusesToListSaysSoRatherThanEmpty()
    {
        using var scratch = new ScratchFolder();
        string refused = scratch.Folder("refused");
        scratch.File("x", "refused", "inside.txt");

        using (DenyListing.On(refused))
        {
            FolderListingResult listing = FolderListing.List(refused);

            Assert.Equal(FolderListingFailure.AccessDenied, listing.Failure);
            Assert.Empty(listing.Entries);
        }
    }

    // A refused sub-folder is still a row of its parent, as File Explorer
    // shows it; only entering it is refused.
    [Fact]
    public void ARefusedSubFolderStillListsInItsParent()
    {
        using var scratch = new ScratchFolder();
        string refused = scratch.Folder("refused");
        scratch.File("x", "shown.txt");

        using (DenyListing.On(refused))
        {
            FolderListingResult listing = FolderListing.List(scratch.Path);

            Assert.True(listing.Succeeded);
            Assert.Equal(["refused", "shown.txt"], listing.Entries.Select(e => e.Name));
        }
    }

    // A path that names a file, not a folder, cannot be listed: it reads as a
    // folder that is not there, as File Explorer says of an address it cannot
    // open as a folder.
    [Fact]
    public void AFileInPlaceOfTheFolderReadsAsAFolderThatIsNotThere()
    {
        using var scratch = new ScratchFolder();
        string file = scratch.File("x", "not-a-folder.txt");

        Assert.Equal(FolderListingFailure.NotFound, FolderListing.List(file).Failure);
    }

    [Fact]
    public void AnEmptyFolderListsNothingAndSucceeds()
    {
        using var scratch = new ScratchFolder();
        string empty = scratch.Folder("empty");

        FolderListingResult listing = FolderListing.List(empty);

        Assert.True(listing.Succeeded);
        Assert.Empty(listing.Entries);
    }

    [Fact]
    public void TheSampleFolderHoldsAnEmptyFolderAndAFileWithAVeryLongName()
    {
        using var scratch = new ScratchFolder();

        string root = SampleFolder.Create(scratch.Path);
        FolderListingResult listing = FolderListing.List(root);

        Assert.Contains(listing.Entries, e => e.IsFolder && e.Name == SampleFolder.EmptyFolderName);
        Assert.Empty(FolderListing.List(Path.Combine(root, SampleFolder.EmptyFolderName)).Entries);
        Assert.Contains(listing.Entries, e => !e.IsFolder && e.Name == SampleFolder.LongFileName);
        Assert.True(SampleFolder.LongFileName.Length > 100);
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
