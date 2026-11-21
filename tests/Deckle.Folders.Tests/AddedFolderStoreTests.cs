using Deckle.Core;
using Xunit;

namespace Deckle.Folders.Tests;

// The added folders persist under the module's data folder and survive a
// broken file.
[Collection("folders provider singleton")]
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
    public void AFolderAddedGetsTheIconTheRulePicksInTheAccentColour()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));

        AddedFolder photos = store.Add(scratch.Folder("Photos"));

        Assert.Equal(new IconChoice(IconSymbol.Photos, IconTint.Accent, false), photos.Icon);
    }

    [Fact]
    public void AChangedIconIsThereWhenTheStoreIsReadAgain()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string folder = scratch.Folder("Factures");
        var store = new AddedFolderStore(data);
        store.Add(folder);
        var chosen = new IconChoice(IconSymbol.Work, IconTint.Green, HasBackground: true);
        int changes = 0;
        store.Changed += () => changes++;

        AddedFolder? changed = store.ChangeIcon(folder.ToUpperInvariant(), chosen);

        Assert.Equal(chosen, changed?.Icon);
        Assert.Equal(1, changes);
        Assert.Equal(chosen, new AddedFolderStore(data).Folders.Single().Icon);
    }

    [Fact]
    public void ChangingTheIconOfAPathNotAddedChangesNothing()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));

        Assert.Null(store.ChangeIcon(scratch.Folder("Elsewhere"), new IconChoice(IconSymbol.Home, IconTint.Red, false)));
        Assert.Empty(store.Folders);
    }

    [Fact]
    public void AFileWrittenBeforeIconsLoadsItsFoldersWithTheDefaultIcon()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string photos = scratch.Folder("Photos");
        string json = $$"""
            {
              "Version": 1,
              "Folders": [ { "Path": {{System.Text.Json.JsonSerializer.Serialize(photos)}}, "AddedAt": "2026-11-10T09:00:00+00:00" } ]
            }
            """;
        File.WriteAllText(Path.Combine(data, AddedFolderStore.FileName), json);

        AddedFolder folder = new AddedFolderStore(data).Folders.Single();

        Assert.Equal(IconChoice.Default, folder.Icon);
        Assert.False(File.Exists(Path.Combine(data, AddedFolderStore.FileName + ".bad")));
    }

    [Fact]
    public void AnIconNameThisBuildDoesNotKnowFallsBackForThatPartAlone()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string folder = scratch.Folder("A");
        string json = $$"""
            {
              "Version": 2,
              "Folders": [ { "Path": {{System.Text.Json.JsonSerializer.Serialize(folder)}}, "AddedAt": "2026-11-10T09:00:00+00:00",
                             "Icon": "Rocket", "Tint": "Green", "IconBackground": true } ]
            }
            """;
        File.WriteAllText(Path.Combine(data, AddedFolderStore.FileName), json);

        AddedFolder read = new AddedFolderStore(data).Folders.Single();

        Assert.Equal(new IconChoice(IconSymbol.Folder, IconTint.Green, true), read.Icon);
    }

    [Fact]
    public void ARemovedFolderIsGoneWhenTheStoreIsReadAgainAndStaysOnTheDisk()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string a = scratch.Folder("A");
        string kept = scratch.File("x", "A", "inside.txt");
        string b = scratch.Folder("B");
        var store = new AddedFolderStore(data);
        store.Add(a);
        store.Add(b);
        int changes = 0;
        store.Changed += () => changes++;

        bool removed = store.Remove(a.ToUpperInvariant() + "\\");

        Assert.True(removed);
        Assert.Equal(1, changes);
        Assert.Equal(["B"], store.Folders.Select(f => f.Name));
        Assert.Equal(["B"], new AddedFolderStore(data).Folders.Select(f => f.Name));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void RemovingAPathNotAddedChangesNothing()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        var store = new AddedFolderStore(data);
        store.Add(scratch.Folder("A"));
        int changes = 0;
        store.Changed += () => changes++;

        Assert.False(store.Remove(scratch.Folder("Elsewhere")));
        Assert.Equal(0, changes);
        Assert.Single(store.Folders);
    }

    [Fact]
    public void AFolderRemovedThenAddedAgainComesBackLast()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        string a = scratch.Folder("A");
        store.Add(a);
        store.Add(scratch.Folder("B"));

        store.Remove(a);
        store.Add(a);

        Assert.Equal(["B", "A"], store.Folders.Select(f => f.Name));
    }

    [Fact]
    public void AFolderThatVanishedIsNotFoundAndAPresentOneIsReachable()
    {
        using var scratch = new ScratchFolder();
        string present = scratch.Folder("Present");
        string gone = scratch.Folder("Gone");
        Directory.Delete(gone);

        Assert.Equal(AddedFolderReachability.Reachable, AddedFolderReach.Check(present));
        Assert.Equal(AddedFolderReachability.NotFound, AddedFolderReach.Check(gone));
        // A file where the folder was is not the folder.
        Assert.Equal(AddedFolderReachability.NotFound, AddedFolderReach.Check(scratch.File("x", "Gone")));
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
