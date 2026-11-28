using System.Text.Json;
using Xunit;

namespace Deckle.Projects.Tests;

// The store writes a file under a scratch folder, so the whole class stands on
// the temporary file system: integration, not unit.
[Collection(ProjectsProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class ProjectStoreTests
{
    private static ProjectStore NewStore(ScratchFolder scratch) => new(scratch.Folder("data"));

    private static PinnedAction Pin(string id, string? variant = null, string label = "Label", string script = "script.ps1") =>
        new(new ActionRef(id, variant), label, script);

    // ── Adding ─────────────────────────────────────────────────────────────

    [Fact]
    public void AProjectAddedLandsInNoCategory()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);

        AddResult result = store.AddProject(scratch.Folder("alpha"));

        Assert.False(result.AlreadyKnown);
        Assert.Null(result.Project.CategoryId);
        Assert.Equal("alpha", result.Project.DisplayName);
        Assert.Equal([result.Project], store.ProjectsIn(null));
    }

    [Fact]
    public void AProjectAddedFromACategoryJoinsIt()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectCategory category = store.CreateCategory("Development");

        ProjectEntry project = store.AddProject(scratch.Folder("alpha"), category.Id).Project;

        Assert.Equal(category.Id, project.CategoryId);
        Assert.Empty(store.ProjectsIn(null));
    }

    [Fact]
    public void AProjectAddedToACategoryThatDoesNotExistLandsInNone()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);

        ProjectEntry project = store.AddProject(scratch.Folder("alpha"), "no-such-category").Project;

        Assert.Null(project.CategoryId);
    }

    [Fact]
    public void AKnownPathAddsNothingAndSaysSoWhateverItsSpelling()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        string path = scratch.Folder("Alpha");
        ProjectEntry first = store.AddProject(path).Project;
        int changes = 0;
        store.Changed += () => changes++;

        AddResult again = store.AddProject(path.ToUpperInvariant() + Path.DirectorySeparatorChar);

        Assert.True(again.AlreadyKnown);
        Assert.Equal(first.Id, again.Project.Id);
        Assert.Single(store.Projects);
        Assert.Equal(0, changes);
    }

    // ── Renaming ───────────────────────────────────────────────────────────

    [Fact]
    public void ANameIsKeptAndThePathNeverChanges()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry project = store.AddProject(scratch.Folder("alpha")).Project;

        ProjectEntry renamed = store.RenameProject(project.Id, "  My alpha  ")!;

        Assert.Equal("My alpha", renamed.DisplayName);
        Assert.Equal(project.Path, renamed.Path);
        Assert.Equal("My alpha", store.Find(project.Id)!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyOrBlankNameBringsTheFoldersNameBack(string? blank)
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry project = store.AddProject(scratch.Folder("alpha")).Project;
        store.RenameProject(project.Id, "Something else");

        ProjectEntry renamed = store.RenameProject(project.Id, blank)!;

        Assert.Null(renamed.Name);
        Assert.Equal("alpha", renamed.DisplayName);
    }

    [Fact]
    public void RenamingToTheSameNameChangesNothing()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry project = store.AddProject(scratch.Folder("alpha")).Project;
        store.RenameProject(project.Id, "Name");
        int changes = 0;
        store.Changed += () => changes++;

        store.RenameProject(project.Id, "Name");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void RenamingAnUnknownProjectAnswersNull()
    {
        using var scratch = new ScratchFolder();
        Assert.Null(NewStore(scratch).RenameProject("missing", "x"));
    }

    // ── Categories ─────────────────────────────────────────────────────────

    [Fact]
    public void CategoriesKeepTheirCreationOrder()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);

        store.CreateCategory("Learning");
        store.CreateCategory("Development");
        store.CreateCategory("Games");

        Assert.Equal(["Learning", "Development", "Games"], store.Categories.Select(c => c.Name));
    }

    [Fact]
    public void ABlankCategoryNameKeepsTheDefaultTitle()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);

        Assert.Equal(ProjectCategory.DefaultName, store.CreateCategory("  ").Name);
        Assert.Equal(ProjectCategory.DefaultName, store.CreateCategory().Name);
    }

    [Fact]
    public void RenamingACategoryKeepsItsProjectsAndABlankNameIsRefused()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectCategory category = store.CreateCategory("Dev");
        ProjectEntry project = store.AddProject(scratch.Folder("alpha"), category.Id).Project;

        ProjectCategory renamed = store.RenameCategory(category.Id, " Development ")!;
        ProjectCategory refused = store.RenameCategory(category.Id, "   ")!;

        Assert.Equal("Development", renamed.Name);
        Assert.Equal("Development", refused.Name);
        Assert.Equal(category.Id, store.Find(project.Id)!.CategoryId);
        Assert.Null(store.RenameCategory("missing", "x"));
    }

    [Fact]
    public void DeletingACategoryReleasesItsProjectsAndRemovesNone()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectCategory category = store.CreateCategory("Dev");
        ProjectCategory other = store.CreateCategory("Games");
        store.AddProject(scratch.Folder("a"), category.Id);
        store.AddProject(scratch.Folder("b"), category.Id);
        ProjectEntry kept = store.AddProject(scratch.Folder("c"), other.Id).Project;

        Assert.True(store.DeleteCategory(category.Id));

        Assert.Equal(["Games"], store.Categories.Select(c => c.Name));
        Assert.Equal(3, store.Projects.Count);
        Assert.Equal(["a", "b"], store.ProjectsIn(null).Select(p => p.DisplayName));
        Assert.Equal(other.Id, store.Find(kept.Id)!.CategoryId);
        Assert.False(store.DeleteCategory(category.Id));
    }

    // ── Moving and ordering ────────────────────────────────────────────────

    [Fact]
    public void AMoveAppendsAtTheEndOfTheTargetAndBackToNone()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectCategory dev = store.CreateCategory("Dev");
        ProjectEntry a = store.AddProject(scratch.Folder("a"), dev.Id).Project;
        ProjectEntry b = store.AddProject(scratch.Folder("b")).Project;
        ProjectEntry c = store.AddProject(scratch.Folder("c")).Project;

        store.MoveProject(c.Id, dev.Id);
        store.MoveProject(b.Id, dev.Id);

        Assert.Equal([a.Id, c.Id, b.Id], store.ProjectsIn(dev.Id).Select(p => p.Id));
        Assert.Empty(store.ProjectsIn(null));

        store.MoveProject(a.Id, null);

        Assert.Equal([a.Id], store.ProjectsIn(null).Select(p => p.Id));
        Assert.Equal([c.Id, b.Id], store.ProjectsIn(dev.Id).Select(p => p.Id));
    }

    [Fact]
    public void AProjectIsInOneCategoryOrInNone()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectCategory dev = store.CreateCategory("Dev");
        ProjectCategory games = store.CreateCategory("Games");
        ProjectEntry a = store.AddProject(scratch.Folder("a"), dev.Id).Project;

        store.MoveProject(a.Id, games.Id);

        Assert.Empty(store.ProjectsIn(dev.Id));
        Assert.Equal([a.Id], store.ProjectsIn(games.Id).Select(p => p.Id));
        Assert.Equal(games.Id, store.Find(a.Id)!.CategoryId);
    }

    [Fact]
    public void MovingToACategoryThatDoesNotExistLeavesTheProjectWhereItWas()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;
        int changes = 0;
        store.Changed += () => changes++;

        store.MoveProject(a.Id, "missing");
        store.MoveProject(a.Id, null);

        Assert.Equal(0, changes);
        Assert.Null(store.Find(a.Id)!.CategoryId);
    }

    [Fact]
    public void ReorderingGivesTheListedOrderIgnoresUnknownIdsAndKeepsTheForgottenAfterIt()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectCategory dev = store.CreateCategory("Dev");
        ProjectEntry a = store.AddProject(scratch.Folder("a"), dev.Id).Project;
        ProjectEntry outside = store.AddProject(scratch.Folder("outside")).Project;
        ProjectEntry b = store.AddProject(scratch.Folder("b"), dev.Id).Project;
        ProjectEntry c = store.AddProject(scratch.Folder("c"), dev.Id).Project;
        ProjectEntry d = store.AddProject(scratch.Folder("d"), dev.Id).Project;

        // c then a, an unknown id, a project of another category (ignored), b
        // and d forgotten: they follow in their old order.
        store.ReorderInCategory(dev.Id, [c.Id, "unknown", a.Id, outside.Id]);

        Assert.Equal([c.Id, a.Id, b.Id, d.Id], store.ProjectsIn(dev.Id).Select(p => p.Id));
        Assert.Equal([outside.Id], store.ProjectsIn(null).Select(p => p.Id));
    }

    [Fact]
    public void ReorderingToTheSameOrderChangesNothing()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectCategory dev = store.CreateCategory("Dev");
        ProjectEntry a = store.AddProject(scratch.Folder("a"), dev.Id).Project;
        ProjectEntry b = store.AddProject(scratch.Folder("b"), dev.Id).Project;
        int changes = 0;
        store.Changed += () => changes++;

        store.ReorderInCategory(dev.Id, [a.Id, b.Id]);

        Assert.Equal(0, changes);
    }

    // ── Removing ───────────────────────────────────────────────────────────

    [Fact]
    public void ARemovedProjectIsGoneAndItsFolderStaysOnTheDisk()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string folder = scratch.Folder("alpha");
        string kept = scratch.File("x", "alpha", "inside.txt");
        var store = new ProjectStore(data);
        ProjectEntry a = store.AddProject(folder).Project;
        store.AddProject(scratch.Folder("beta"));

        Assert.True(store.RemoveProject(a.Id));

        Assert.Equal(["beta"], store.Projects.Select(p => p.DisplayName));
        Assert.Equal(["beta"], new ProjectStore(data).Projects.Select(p => p.DisplayName));
        Assert.True(File.Exists(kept));
        Assert.False(store.RemoveProject(a.Id));
    }

    // ── Pins ───────────────────────────────────────────────────────────────

    [Fact]
    public void PinningTwiceIsOnePin()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;

        store.Pin(a.Id, Pin("build-run.0", label: "Build & run"));
        ProjectEntry after = store.Pin(a.Id, Pin("build-run.0", label: "Build & run"))!;

        Assert.Single(after.Pinned);
    }

    [Fact]
    public void ThePinsOfTwoVariantsOfOneEntryAreTwoPins()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;

        store.Pin(a.Id, Pin("self-check.0", "Debug"));
        ProjectEntry after = store.Pin(a.Id, Pin("self-check.0", "Release"))!;

        Assert.Equal(["Debug", "Release"], after.Pinned.Select(p => p.Action.Variant));
    }

    [Fact]
    public void UnpinningTheMainActionClearsTheMain()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;
        store.Pin(a.Id, Pin("build-run.0", label: "Build & run"));
        store.Pin(a.Id, Pin("self-check.0", "Debug"));
        Assert.True(store.SetMain(a.Id, new ActionRef("build-run.0")));
        Assert.Equal("Build & run", store.Find(a.Id)!.MainAction!.Label);

        ProjectEntry after = store.Unpin(a.Id, new ActionRef("build-run.0"))!;

        Assert.Null(after.Main);
        Assert.Null(after.MainAction);
        Assert.Equal(["self-check.0"], after.Pinned.Select(p => p.Action.Id));
    }

    [Fact]
    public void UnpinningAnotherActionKeepsTheMain()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;
        store.Pin(a.Id, Pin("build-run.0"));
        store.Pin(a.Id, Pin("self-check.0", "Debug"));
        store.SetMain(a.Id, new ActionRef("build-run.0"));

        ProjectEntry after = store.Unpin(a.Id, new ActionRef("self-check.0", "Debug"))!;

        Assert.Equal(new ActionRef("build-run.0"), after.Main);
    }

    [Fact]
    public void AMainActionThatIsNotPinnedIsRefused()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;

        Assert.False(store.SetMain(a.Id, new ActionRef("build-run.0")));
        Assert.Null(store.Find(a.Id)!.Main);
        Assert.False(store.SetMain("missing", null));
    }

    [Fact]
    public void ClearingTheMainWithNullIsAccepted()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;
        store.Pin(a.Id, Pin("build-run.0"));
        store.SetMain(a.Id, new ActionRef("build-run.0"));

        Assert.True(store.SetMain(a.Id, null));
        Assert.Null(store.Find(a.Id)!.Main);
    }

    [Fact]
    public void ARefreshUpdatesTheLabelOfAPinWhoseScriptIsUnchanged()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;
        store.Pin(a.Id, Pin("build-run.0", label: "Build", script: "build-run.ps1"));
        IReadOnlyList<ScriptMenuEntry> listing = [new("build-run.0", "Build & run", "build-run.ps1", "", [])];

        ProjectEntry after = store.RefreshPinned(a.Id, listing)!;

        Assert.Equal(new PinnedAction(new ActionRef("build-run.0"), "Build & run", "build-run.ps1"), after.Pinned[0]);
    }

    [Fact]
    public void ARefreshLeavesAVanishedPinAndAPinWhoseIdentifierNowNamesAnotherScriptAsTheyWere()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;
        store.Pin(a.Id, Pin("build-run.0", label: "Build", script: "build-run.ps1"));
        store.Pin(a.Id, Pin("old.0", label: "Old thing", script: "old.ps1"));
        store.Pin(a.Id, Pin("self-check.0", "Debug", "Self-check", "self-check.ps1"));
        IReadOnlyList<ScriptMenuEntry> listing =
        [
            new("build-run.0", "Build & run", "build-run2.ps1", "", []),
            // self-check no longer has a Debug variant: the pin is not given.
            new("self-check.0", "Self-check", "self-check.ps1", "", ["Release"]),
        ];

        ProjectEntry after = store.RefreshPinned(a.Id, listing)!;

        Assert.Equal(new PinnedAction(new ActionRef("build-run.0"), "Build", "build-run.ps1"), after.Pinned[0]);
        Assert.Equal(new PinnedAction(new ActionRef("old.0"), "Old thing", "old.ps1"), after.Pinned[1]);
        Assert.Equal(new PinnedAction(new ActionRef("self-check.0", "Debug"), "Self-check", "self-check.ps1"), after.Pinned[2]);
    }

    [Fact]
    public void ARefreshThatChangesNothingRaisesNothing()
    {
        using var scratch = new ScratchFolder();
        var store = NewStore(scratch);
        ProjectEntry a = store.AddProject(scratch.Folder("a")).Project;
        store.Pin(a.Id, Pin("build-run.0", label: "Build", script: "build-run.ps1"));
        int changes = 0;
        store.Changed += () => changes++;

        store.RefreshPinned(a.Id, [new("build-run.0", "Build", "build-run.ps1", "", [])]);

        Assert.Equal(0, changes);
    }

    // ── Persistence ────────────────────────────────────────────────────────

    [Fact]
    public void WhatWasWrittenComesBackWhenTheStoreIsReadAgain()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        var store = new ProjectStore(data);
        ProjectCategory dev = store.CreateCategory("Development");
        store.CreateCategory("Leisure");
        ProjectEntry a = store.AddProject(scratch.Folder("a"), dev.Id).Project;
        store.AddProject(scratch.Folder("b"));
        store.RenameProject(a.Id, "Alpha");
        store.Pin(a.Id, Pin("build-run.0", label: "Build & run", script: "build-run.ps1"));
        store.Pin(a.Id, Pin("self-check.0", "Debug", "Self-check", "self-check.ps1"));
        store.SetMain(a.Id, new ActionRef("build-run.0"));

        var again = new ProjectStore(data);

        Assert.Equal(store.Categories, again.Categories);
        Assert.Equal(store.Projects.Count, again.Projects.Count);
        for (int i = 0; i < store.Projects.Count; i++)
        {
            ProjectEntry x = store.Projects[i];
            ProjectEntry y = again.Projects[i];
            Assert.Equal(x with { Pinned = [] }, y with { Pinned = [] });
            Assert.Equal(x.Pinned, y.Pinned);
        }
    }

    [Fact]
    public void AFileWrittenByALaterVersionWithUnknownFieldsStillLoads()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string folder = scratch.Folder("alpha");
        string json = $$"""
            {
              "Version": 7,
              "Theme": "unknown",
              "Categories": [ { "Id": "c1", "Name": "Development", "Colour": "red" } ],
              "Projects": [ { "Id": "p1", "Path": {{JsonSerializer.Serialize(folder)}}, "CategoryId": "c1",
                              "AddedAt": "2026-11-15T09:00:00+00:00", "Flag": true } ]
            }
            """;
        File.WriteAllText(Path.Combine(data, ProjectStore.FileName), json);

        var store = new ProjectStore(data);

        Assert.Equal("Development", store.Categories.Single().Name);
        Assert.Equal("c1", store.Projects.Single().CategoryId);
        Assert.False(File.Exists(Path.Combine(data, ProjectStore.FileName + ".bad")));
    }

    [Fact]
    public void AFileThatReferencesWhatIsNotThereIsRepairedNotDropped()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string folder = scratch.Folder("alpha");
        string json = $$"""
            {
              "Version": 1,
              "Categories": [],
              "Projects": [ { "Id": "p1", "Path": {{JsonSerializer.Serialize(folder)}}, "CategoryId": "gone",
                              "Pinned": [ { "Id": "a.0", "Label": "A", "Script": "a.ps1" } ],
                              "Main": { "Id": "not-pinned" }, "AddedAt": "2026-11-15T09:00:00+00:00" } ]
            }
            """;
        File.WriteAllText(Path.Combine(data, ProjectStore.FileName), json);

        ProjectEntry project = new ProjectStore(data).Projects.Single();

        Assert.Null(project.CategoryId);
        Assert.Null(project.Main);
        Assert.Single(project.Pinned);
    }

    [Fact]
    public void AnUnreadableFileIsCopiedAsideAndTheStoreStartsEmpty()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        string file = Path.Combine(data, ProjectStore.FileName);
        File.WriteAllText(file, "{ this is not json");

        var store = new ProjectStore(data);

        Assert.Empty(store.Projects);
        Assert.Empty(store.Categories);
        Assert.Equal("{ this is not json", File.ReadAllText(file + ".bad"));
    }

    [Fact]
    public void ASaveThatFailsKeepsTheChangeInMemory()
    {
        using var scratch = new ScratchFolder();
        // A file where the module's folder should be: the folder cannot be created.
        string blocker = scratch.File("not a folder", "blocker");
        var store = new ProjectStore(blocker);

        AddResult result = store.AddProject(scratch.Folder("alpha"));

        Assert.False(result.AlreadyKnown);
        Assert.Single(store.Projects);
    }
}
