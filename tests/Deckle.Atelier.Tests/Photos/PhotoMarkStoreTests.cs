using Xunit;

namespace Deckle.Atelier.Tests;

// Marking a photo changes nothing on the disk next to it, and the marks come
// back when the folder is opened again.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class PhotoMarkStoreTests
{
    [Fact]
    public void MarkingChangesNothingInThePhotoFolder()
    {
        using var scratch = new ScratchFolder();
        string shoot = scratch.Folder("Shoot");
        string photo = scratch.File("x", "Shoot", "DSC_0001.NEF");
        DateTime written = File.GetLastWriteTimeUtc(photo);
        var store = new PhotoMarkStore(scratch.Folder("data"), shoot);

        store.Mark("DSC_0001.NEF", PhotoMark.Delete);

        Assert.Equal([photo], Directory.GetFileSystemEntries(shoot));
        Assert.Equal(written, File.GetLastWriteTimeUtc(photo));
        Assert.False(FolderPaths.IsInside(shoot, store.FilePath));
    }

    [Fact]
    public void TheMarksComeBackWhenTheFolderIsOpenedAgain()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string shoot = scratch.Folder("Shoot");

        var store = new PhotoMarkStore(data, shoot);
        store.Mark(["a.NEF", "b.NEF"], PhotoMark.Keep);
        store.Mark("c.NEF", PhotoMark.Delete);
        store.Mark("b.NEF", PhotoMark.None);

        var reread = new PhotoMarkStore(data, shoot);
        Assert.Equal(PhotoMark.Keep, reread.MarkOf("A.nef"));
        Assert.Equal(PhotoMark.None, reread.MarkOf("b.NEF"));
        Assert.Equal(PhotoMark.Delete, reread.MarkOf("c.NEF"));
    }

    [Fact]
    public void EachFolderKeepsItsOwnMarks()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");

        new PhotoMarkStore(data, scratch.Folder("A")).Mark("x.jpg", PhotoMark.Keep);

        Assert.Equal(PhotoMark.None, new PhotoMarkStore(data, scratch.Folder("B")).MarkOf("x.jpg"));
    }

    [Fact]
    public void TheCountersCountTheFolderAsItIs()
    {
        using var scratch = new ScratchFolder();
        var store = new PhotoMarkStore(scratch.Folder("data"), scratch.Folder("Shoot"));
        store.Mark("a.NEF", PhotoMark.Keep);
        store.Mark("b.NEF", PhotoMark.Delete);
        store.Mark("gone.NEF", PhotoMark.Delete);
        PhotoFile[] photos = [Photo("a.NEF"), Photo("b.NEF"), Photo("c.NEF"), Photo("d.NEF")];

        Assert.Equal(new MarkCounts(Unmarked: 2, Keep: 1, Delete: 1), store.Count(photos));
    }

    [Fact]
    public void AGestureThatChangesNothingRaisesNothing()
    {
        using var scratch = new ScratchFolder();
        var store = new PhotoMarkStore(scratch.Folder("data"), scratch.Folder("Shoot"));
        store.Mark("a.NEF", PhotoMark.Keep);
        int changes = 0;
        store.Changed += () => changes++;

        store.Mark("a.NEF", PhotoMark.Keep);
        store.Mark("b.NEF", PhotoMark.None);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void ABrokenFileIsSetAsideAndTheFolderOpensUnmarked()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string shoot = scratch.Folder("Shoot");
        var first = new PhotoMarkStore(data, shoot);
        first.Mark("a.NEF", PhotoMark.Keep);
        File.WriteAllText(first.FilePath, "{ not json");

        var reread = new PhotoMarkStore(data, shoot);

        Assert.Equal(PhotoMark.None, reread.MarkOf("a.NEF"));
        Assert.True(File.Exists(first.FilePath + ".bad"));
    }

    private static PhotoFile Photo(string name) => new(name, Path.Combine("C:\\Shoot", name), 1, DateTime.Now);
}
