using Xunit;

namespace Deckle.Paste.Tests;

// The chosen folder persists in the data directory and survives a missing or
// broken file.
[Collection("paste provider singleton")]
[Trait("Category", "integration")]
public sealed class PasteFolderStoreTests
{
    [Fact]
    public void NoFolderIsChosenBeforeTheFirstChoice()
    {
        using var scratch = new ScratchFolder();

        Assert.Null(new PasteFolderStore(scratch.Folder("data")).Folder);
    }

    [Fact]
    public void AChosenFolderIsThereWhenTheStoreIsReadAgain()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string chosen = scratch.Folder("Screenshots");
        var store = new PasteFolderStore(data);

        store.SetFolder(chosen);

        Assert.Equal(chosen, store.Folder);
        Assert.Equal(chosen, new PasteFolderStore(data).Folder);
    }

    [Fact]
    public void ALaterChoiceReplacesTheEarlierOne()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        var store = new PasteFolderStore(data);

        store.SetFolder(scratch.Folder("A"));
        store.SetFolder(scratch.Folder("B"));

        Assert.Equal(Path.Combine(scratch.Path, "B"), new PasteFolderStore(data).Folder);
    }

    [Fact]
    public void ADataDirectoryNotYetCreatedIsCreatedAtTheFirstChoice()
    {
        using var scratch = new ScratchFolder();
        string data = Path.Combine(scratch.Path, "modules", "paste");
        string chosen = scratch.Folder("A");

        new PasteFolderStore(data).SetFolder(chosen);

        Assert.Equal(chosen, new PasteFolderStore(data).Folder);
    }

    [Fact]
    public void AnUnreadableFileStartsWithNoFolderAndIsKeptAside()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        File.WriteAllText(Path.Combine(data, PasteFolderStore.FileName), "{ not json");

        var store = new PasteFolderStore(data);

        Assert.Null(store.Folder);
        Assert.True(File.Exists(Path.Combine(data, PasteFolderStore.FileName + ".bad")));
    }

    [Fact]
    public void AFolderChosenAfterABrokenFileIsKept()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        File.WriteAllText(Path.Combine(data, PasteFolderStore.FileName), "[]");
        string chosen = scratch.Folder("A");

        new PasteFolderStore(data).SetFolder(chosen);

        Assert.Equal(chosen, new PasteFolderStore(data).Folder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AnEmptyFolderIsRefused(string folder)
    {
        using var scratch = new ScratchFolder();

        Assert.Throws<ArgumentException>(() => new PasteFolderStore(scratch.Folder("data")).SetFolder(folder));
    }
}
