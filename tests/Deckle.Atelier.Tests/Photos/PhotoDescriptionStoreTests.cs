using Xunit;
using Deckle.Core;

namespace Deckle.Atelier.Tests;

// The descriptions sit in the module's store, never next to the photos, come
// back after a restart, and a photo renamed or deleted outside the app breaks
// nothing.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class PhotoDescriptionStoreTests
{
    static readonly DateTimeOffset Written = new(2026, 11, 15, 10, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void ADescriptionComesBackAfterARestartAndNothingIsWrittenNextToThePhotos()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string shoot = scratch.Folder("Shoot");
        string photo = scratch.File("x", "Shoot", "DSC_0001.JPG");

        var store = new PhotoDescriptionStore(data, shoot);
        store.Set("DSC_0001.JPG", new PhotoDescription("Un chat sur un mur.", "vision-model", Written));

        var reread = new PhotoDescriptionStore(data, shoot);
        PhotoDescription? description = reread.DescriptionOf("dsc_0001.jpg");
        Assert.NotNull(description);
        Assert.Equal("Un chat sur un mur.", description.Text);
        Assert.Equal("vision-model", description.Model);
        Assert.Equal(Written, description.WrittenAt);
        Assert.Equal([photo], Directory.GetFileSystemEntries(shoot));
        Assert.False(FolderPaths.IsInside(shoot, store.FilePath));
    }

    [Fact]
    public void APhotoRenamedOrDeletedOutsideTheAppBreaksNothing()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string shoot = scratch.Folder("Shoot");
        string a = scratch.File("x", "Shoot", "a.JPG");
        string b = scratch.File("x", "Shoot", "b.JPG");
        var store = new PhotoDescriptionStore(data, shoot);
        store.Set("a.JPG", new PhotoDescription("A", "m", Written));
        store.Set("b.JPG", new PhotoDescription("B", "m", Written));

        File.Move(a, Path.Combine(shoot, "renamed.JPG"));
        File.Delete(b);

        var reread = new PhotoDescriptionStore(data, shoot);
        IReadOnlyList<PhotoFile> photos = PhotoFolder.List(shoot).Photos;
        Assert.Equal(0, reread.CountDescribed(photos));
        Assert.Null(reread.DescriptionOf("renamed.JPG"));
        reread.Set("renamed.JPG", new PhotoDescription("A again", "m", Written));
        Assert.Equal(1, new PhotoDescriptionStore(data, shoot).CountDescribed(photos));
    }

    [Fact]
    public void ARenameInTheAppCarriesTheDescriptionAndARecycleDropsIt()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string shoot = scratch.Folder("Shoot");
        var store = new PhotoDescriptionStore(data, shoot);
        store.Set("a.JPG", new PhotoDescription("A", "m", Written));
        store.Set("b.JPG", new PhotoDescription("B", "m", Written));

        store.Settle(removed: ["b.JPG"], renamed: [("a.JPG", "2026-11-15 a.JPG")]);

        var reread = new PhotoDescriptionStore(data, shoot);
        Assert.Equal("A", reread.DescriptionOf("2026-11-15 a.JPG")?.Text);
        Assert.False(reread.IsDescribed("a.JPG"));
        Assert.False(reread.IsDescribed("b.JPG"));
    }

    [Fact]
    public void AnUnreadableFileIsKeptAsideAndTheFolderStartsWithoutDescriptions()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string shoot = scratch.Folder("Shoot");
        string path = new PhotoDescriptionStore(data, shoot).FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var store = new PhotoDescriptionStore(data, shoot);

        Assert.False(store.IsDescribed("a.JPG"));
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void EachFolderKeepsItsOwnDescriptions()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        new PhotoDescriptionStore(data, scratch.Folder("One")).Set("a.JPG", new PhotoDescription("A", "m", Written));

        Assert.False(new PhotoDescriptionStore(data, scratch.Folder("Two")).IsDescribed("a.JPG"));
    }
}

// The model that describes photos is one stored choice; with nothing stored
// there is none.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class PhotoDescriptionModelChoiceStoreTests
{
    [Fact]
    public void NothingStoredIsNoChoice()
    {
        using var scratch = new ScratchFolder();
        Assert.Null(new PhotoDescriptionModelChoiceStore(scratch.Path).Read());
    }

    [Fact]
    public void AChoiceIsReadBackAndCanBeCleared()
    {
        using var scratch = new ScratchFolder();
        var store = new PhotoDescriptionModelChoiceStore(scratch.Path);

        store.Write(new PhotoDescriptionModelChoice("ollama-local", "some-vision-model"));
        Assert.Equal(new PhotoDescriptionModelChoice("ollama-local", "some-vision-model"),
            new PhotoDescriptionModelChoiceStore(scratch.Path).Read());

        store.Write(null);
        Assert.Null(store.Read());
    }

    [Fact]
    public void AChoiceWithoutAModelIsNoChoice()
    {
        using var scratch = new ScratchFolder();
        var store = new PhotoDescriptionModelChoiceStore(scratch.Path);
        File.WriteAllText(store.FilePath, """{"ProviderId":"ollama-local","Model":""}""");

        Assert.Null(store.Read());
    }
}
