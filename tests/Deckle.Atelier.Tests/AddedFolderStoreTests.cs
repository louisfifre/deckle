using Xunit;

namespace Deckle.Atelier.Tests;

// The added folders persist under the module's data folder and survive a
// broken file.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class AddedFolderStoreTests
{
    [Fact]
    public void AFolderAddedIsThereWhenTheStoreIsReadAgain()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string photos = scratch.Folder("Photos");

        new AddedFolderStore(data).Add(photos);

        var reread = new AddedFolderStore(data);
        Assert.Equal([FolderPaths.Normalize(photos)], reread.Folders.Select(f => f.Path));
    }

    [Fact]
    public void AFolderJustAddedComesLastAndAddingItAgainKeepsOne()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        string a = scratch.Folder("A");
        string b = scratch.Folder("B");

        store.Add(b);
        store.Add(a);
        AddedFolder again = store.Add(a.ToUpperInvariant() + "\\");

        Assert.Equal(["B", "A"], store.Folders.Select(f => f.Name));
        Assert.Equal(FolderPaths.Normalize(a), again.Path, ignoreCase: true);
    }

    [Fact]
    public void AnUnreadableFileStartsEmptyAndIsKeptAside()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        File.WriteAllText(Path.Combine(data, AddedFolderStore.FileName), "{ not json");

        var store = new AddedFolderStore(data);

        Assert.Empty(store.Folders);
        Assert.True(File.Exists(Path.Combine(data, AddedFolderStore.FileName + ".bad")));
    }

    [Fact]
    public void ThePathBelongsToTheDeepestAddedFolderThatHoldsIt()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        string outer = scratch.Folder("Outer");
        string inner = scratch.Folder("Outer", "Inner");
        store.Add(outer);
        store.Add(inner);

        Assert.Equal("Inner", store.Owning(Path.Combine(inner, "x"))!.Name);
        Assert.Equal("Outer", store.Owning(outer)!.Name);
        Assert.Null(store.Owning(scratch.Folder("Elsewhere")));
    }
}
