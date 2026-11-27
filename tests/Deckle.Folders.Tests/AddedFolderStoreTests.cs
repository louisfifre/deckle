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
    public void AToolChosenForAFolderIsThereWhenTheStoreIsReadAgain()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string a = scratch.Folder("A");
        string b = scratch.Folder("B");
        var store = new AddedFolderStore(data);
        store.Add(a);
        store.Add(b);
        int changes = 0;
        store.Changed += () => changes++;

        AddedFolder? changed = store.ChangeTool(a.ToUpperInvariant() + "\\", "photos");

        Assert.Equal("photos", changed?.Tool);
        Assert.Equal(1, changes);
        var reread = new AddedFolderStore(data);
        Assert.Equal("photos", reread.Find(a)?.Tool);
        Assert.Null(reread.Find(b)?.Tool);
    }

    [Fact]
    public void AFolderAddedHasNoToolAndNeitherDoesOneReadFromAVersion2File()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string added = scratch.Folder("Added");
        string old = scratch.Folder("Old");
        Assert.Null(new AddedFolderStore(scratch.Folder("fresh")).Add(added).Tool);
        string json = $$"""
            {
              "Version": 2,
              "Folders": [ { "Path": {{System.Text.Json.JsonSerializer.Serialize(old)}}, "AddedAt": "2026-11-10T09:00:00+00:00",
                             "Icon": "Folder", "Tint": "Accent", "IconBackground": false } ]
            }
            """;
        File.WriteAllText(Path.Combine(data, AddedFolderStore.FileName), json);

        AddedFolder read = new AddedFolderStore(data).Folders.Single();

        Assert.Null(read.Tool);
        Assert.False(File.Exists(Path.Combine(data, AddedFolderStore.FileName + ".bad")));
    }

    [Fact]
    public void ChangingTheToolOfAPathNotAddedReturnsNullAndWritesNothing()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        var store = new AddedFolderStore(data);
        int changes = 0;
        store.Changed += () => changes++;

        Assert.Null(store.ChangeTool(scratch.Folder("Elsewhere"), "photos"));

        Assert.Equal(0, changes);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void ChoosingTheDefaultToolAfterAnotherClearsIt()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string a = scratch.Folder("A");
        var store = new AddedFolderStore(data);
        store.Add(a);
        store.ChangeTool(a, "sizes");

        AddedFolder? cleared = store.ChangeTool(a, null);

        Assert.Null(cleared?.Tool);
        Assert.Null(new AddedFolderStore(data).Find(a)?.Tool);
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

    [Fact]
    public void AGroupMadeAndRenamedAndItsFoldersAreThereWhenTheStoreIsReadAgain()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string a = scratch.Folder("A");
        var store = new AddedFolderStore(data);
        store.Add(a);
        FolderGroup made = store.AddGroup("  Travail ");
        store.MoveToGroup(a, made.Id);
        int changes = 0;
        store.Changed += () => changes++;

        FolderGroup? renamed = store.RenameGroup(made.Id, "Work");

        Assert.Equal(made.Id, renamed?.Id);
        Assert.Equal(1, changes);
        var reread = new AddedFolderStore(data);
        FolderGroup group = Assert.Single(reread.Groups);
        Assert.Equal(new FolderGroup(made.Id, "Work"), group);
        Assert.Equal(made.Id, reread.Find(a)?.GroupId);
    }

    [Fact]
    public void GroupsKeepTheOrderTheyWereMade()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));

        store.AddGroup("B");
        store.AddGroup("A");

        Assert.Equal(["B", "A"], store.Groups.Select(g => g.Name));
    }

    [Fact]
    public void RemovingAGroupKeepsItsFoldersInNoGroupWhereTheyWere()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        var store = new AddedFolderStore(data);
        store.Add(scratch.Folder("A"));
        store.Add(scratch.Folder("B"));
        store.Add(scratch.Folder("C"));
        FolderGroup group = store.AddGroup("Work");
        store.MoveToGroup(scratch.Folder("A"), group.Id);
        store.MoveToGroup(scratch.Folder("C"), group.Id);
        int changes = 0;
        store.Changed += () => changes++;

        bool removed = store.RemoveGroup(group.Id);

        Assert.True(removed);
        Assert.Equal(1, changes);
        var reread = new AddedFolderStore(data);
        Assert.Empty(reread.Groups);
        Assert.Equal(["B", "A", "C"], reread.Folders.Select(f => f.Name));
        Assert.All(reread.Folders, f => Assert.Null(f.GroupId));
        Assert.False(store.RemoveGroup(group.Id));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AFolderMovedComesLastOfThePlaceItJoins()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        var store = new AddedFolderStore(data);
        foreach (string name in new[] { "A", "B", "C", "D" }) store.Add(scratch.Folder(name));
        FolderGroup group = store.AddGroup("Work");
        store.MoveToGroup(scratch.Folder("B"), group.Id);

        store.MoveToGroup(scratch.Folder("A"), group.Id);
        FolderGrouping into = FolderGrouping.Of(store.Folders, store.Groups);
        store.MoveToGroup(scratch.Folder("B"), null);
        FolderGrouping outOf = FolderGrouping.Of(new AddedFolderStore(data).Folders, store.Groups);

        Assert.Equal(["B", "A"], into.Groups.Single().Folders.Select(f => f.Name));
        Assert.Equal(["C", "D", "B"], outOf.Ungrouped.Select(f => f.Name));
        Assert.Equal(["A"], outOf.Groups.Single().Folders.Select(f => f.Name));
    }

    [Fact]
    public void MovingToThePlaceAFolderIsInAlreadyChangesNothing()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        store.Add(scratch.Folder("A"));
        store.Add(scratch.Folder("B"));
        int changes = 0;
        store.Changed += () => changes++;

        AddedFolder? same = store.MoveToGroup(scratch.Folder("A"), null);

        Assert.Equal("A", same?.Name);
        Assert.Equal(0, changes);
        Assert.Equal(["A", "B"], store.Folders.Select(f => f.Name));
    }

    [Fact]
    public void ANameAlreadyTakenIsRefusedWhateverItsCaseAndAnEmptyOneToo()
    {
        using var scratch = new ScratchFolder();
        var store = new AddedFolderStore(scratch.Folder("data"));
        FolderGroup work = store.AddGroup("Work");
        FolderGroup family = store.AddGroup("Family");

        Assert.True(store.IsGroupNameTaken(" WORK "));
        Assert.False(store.IsGroupNameTaken("WORK", work.Id));
        Assert.Throws<ArgumentException>(() => store.AddGroup("work"));
        Assert.Throws<ArgumentException>(() => store.RenameGroup(family.Id, "WORK"));
        Assert.Throws<ArgumentException>(() => store.AddGroup("   "));
        Assert.Equal(["Work", "Family"], store.Groups.Select(g => g.Name));
        // Another case of its own name is a rename, not a clash.
        Assert.Equal("WORK", store.RenameGroup(work.Id, "WORK")?.Name);
        Assert.Null(store.RenameGroup("nope", "Anything"));
    }

    [Fact]
    public void MovingToAGroupThatDoesNotExistReturnsNullAndChangesNothing()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        var store = new AddedFolderStore(data);
        store.Add(scratch.Folder("A"));
        int changes = 0;
        store.Changed += () => changes++;

        Assert.Null(store.MoveToGroup(scratch.Folder("A"), "nope"));
        Assert.Null(store.MoveToGroup(scratch.Folder("Elsewhere"), null));

        Assert.Equal(0, changes);
        Assert.Null(store.Folders.Single().GroupId);
        Assert.Null(new AddedFolderStore(data).Folders.Single().GroupId);
    }

    [Fact]
    public void AFolderThatNamesAnUnknownGroupIsReadAsInNoGroup()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string a = scratch.Folder("A");
        string b = scratch.Folder("B");
        string json = $$"""
            {
              "Version": 3,
              "Groups": [ { "Id": "w", "Name": "Work" } ],
              "Folders": [
                { "Path": {{System.Text.Json.JsonSerializer.Serialize(a)}}, "AddedAt": "2026-11-10T09:00:00+00:00", "Group": "gone" },
                { "Path": {{System.Text.Json.JsonSerializer.Serialize(b)}}, "AddedAt": "2026-11-10T09:00:00+00:00", "Group": "w" } ]
            }
            """;
        File.WriteAllText(Path.Combine(data, AddedFolderStore.FileName), json);

        var store = new AddedFolderStore(data);

        Assert.Null(store.Find(a)?.GroupId);
        Assert.Equal("w", store.Find(b)?.GroupId);
    }

    [Fact]
    public void AGroupWithNoIdOrNameOrOneRepeatedIsSkippedWhenTheFileIsRead()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string json = """
            {
              "Version": 3,
              "Groups": [
                { "Id": "a", "Name": "Work" },
                { "Id": "", "Name": "No id" },
                { "Id": "b", "Name": " " },
                { "Id": "a", "Name": "Same id" },
                { "Id": "c", "Name": "WORK" },
                { "Id": "d", "Name": "Family" } ],
              "Folders": []
            }
            """;
        File.WriteAllText(Path.Combine(data, AddedFolderStore.FileName), json);

        var store = new AddedFolderStore(data);

        Assert.Equal(["Work", "Family"], store.Groups.Select(g => g.Name));
    }

    [Fact]
    public void AVersion2FileHasNoGroups()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string json = $$"""
            {
              "Version": 2,
              "Folders": [ { "Path": {{System.Text.Json.JsonSerializer.Serialize(scratch.Folder("Old"))}}, "AddedAt": "2026-11-10T09:00:00+00:00" } ]
            }
            """;
        File.WriteAllText(Path.Combine(data, AddedFolderStore.FileName), json);

        var store = new AddedFolderStore(data);

        Assert.Empty(store.Groups);
        Assert.Null(store.Folders.Single().GroupId);
    }
}
