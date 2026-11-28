using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Projects.Tests;

// The store writes counts and error types, never a project's path or name, a
// category's title or an action's id, and a save that fails is one Warning.
[Collection(ProjectsProviderCollection.Name)]
[Trait("Category", "observability")]
public sealed class ProjectStoreEventsTests
{
    [Fact]
    public void NoEventOfTheStoreHoldsAPathAProjectNameACategoryTitleOrAnActionId()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("Secret-Project-Folder");
        using var listener = new TestEventListener("Deckle-Projects");
        var store = new ProjectStore(scratch.Folder("data"));

        ProjectCategory category = store.CreateCategory("Secret category");
        ProjectEntry project = store.AddProject(folder, category.Id).Project;
        store.AddProject(folder);
        store.RenameProject(project.Id, "Secret name");
        store.Pin(project.Id, new PinnedAction(new ActionRef("secret-action.0"), "Secret label", "secret.ps1"));
        store.MoveProject(project.Id, null);
        store.DeleteCategory(category.Id);
        store.RemoveProject(project.Id);

        string[] secrets = ["Secret", "secret", scratch.Path];
        string[] texts = [.. listener.Events
            .SelectMany(e => (e.Payload ?? []).OfType<string>().Concat([e.Message ?? ""]))];
        Assert.NotEmpty(listener.Events);
        Assert.DoesNotContain(texts, t => secrets.Any(s => t.Contains(s, StringComparison.Ordinal)));
    }

    [Fact]
    public void AddingAKnownFolderIsWrittenAsKnownNotAsAdded()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("alpha");
        var store = new ProjectStore(scratch.Folder("data"));
        store.AddProject(folder);
        using var listener = new TestEventListener("Deckle-Projects");

        store.AddProject(folder);

        EventWrittenEventArgs written = Assert.Single(listener.Events);
        Assert.Equal(DeckleProjectsSource.EvtProjectAlreadyKnown, written.EventId);
    }

    [Fact]
    public void ASaveThatFailsIsOneWarningWithTheErrorType()
    {
        using var scratch = new ScratchFolder();
        string blocker = scratch.File("not a folder", "blocker");
        var store = new ProjectStore(blocker);
        using var listener = new TestEventListener("Deckle-Projects");

        store.AddProject(scratch.Folder("alpha"));

        EventWrittenEventArgs warning = Assert.Single(listener.Events, e => e.Level == EventLevel.Warning);
        Assert.Equal(DeckleProjectsSource.EvtStoreSaveFailed, warning.EventId);
        Assert.False(string.IsNullOrEmpty(warning.Payload?[0] as string));
    }

    [Fact]
    public void AnUnreadableFileIsOneWarning()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        File.WriteAllText(Path.Combine(data, ProjectStore.FileName), "{ broken");
        using var listener = new TestEventListener("Deckle-Projects");

        _ = new ProjectStore(data);

        EventWrittenEventArgs warning = Assert.Single(listener.Events, e => e.Level == EventLevel.Warning);
        Assert.Equal(DeckleProjectsSource.EvtStoreLoadFailed, warning.EventId);
    }
}
