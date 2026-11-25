using Xunit;

namespace Deckle.Projects.Tests;

// The rules that cross the parts, with a terminal that is not one, a reader of
// folders that answers what the test says and a menu reader that starts no shell.
[Collection("projects provider singleton")]
[Trait("Category", "integration")]
public sealed class ProjectsWorkspaceTests : IDisposable
{
    private readonly ScratchFolder _scratch = new();
    private readonly FakeTerminalLauncher _launcher = new();
    private readonly CountingReader _reader = new();
    private readonly StubMenuReader _menus = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly ProjectsWorkspace _workspace;

    public ProjectsWorkspaceTests()
    {
        _workspace = new ProjectsWorkspace(
            _scratch.Folder("data"), _clock, _launcher, new FakeOpener(), _reader, _menus,
            shell: @"C:\fake\pwsh.exe", watchRuns: false);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scratch.Dispose();
    }

    private sealed class CountingReader : IProjectReader
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public Task<ProjectReading> ReadAsync(string projectPath, CancellationToken cancellation)
        {
            Interlocked.Increment(ref _reads);
            return Task.FromResult(new ProjectReading(
                FolderState.Present, RepositoryState.Repository, VersionReading.None, 0, null, false, DateTimeOffset.UnixEpoch));
        }
    }

    private sealed class StubMenuReader : IScriptMenuReader
    {
        public ScriptMenuListing Answer { get; set; } = new ScriptMenuListing.NoMenu();

        public int Reads { get; private set; }

        public Task<ScriptMenuListing> ListAsync(string projectPath, CancellationToken cancellation)
        {
            Reads++;
            return Task.FromResult(Answer);
        }
    }

    private sealed class FakeOpener : IProjectOpener
    {
        public bool OpenInExplorer(string projectPath) => true;
        public bool OpenInTerminal(string projectPath) => true;
        public bool OpenScriptMenu(string projectPath) => true;
        public bool OpenWebPage(Uri page) => true;
    }

    private ProjectEntry AddWithMenu(string name)
    {
        string folder = _scratch.Folder(name);
        _scratch.File("# stub", name, "scripts", "menu.ps1");
        ProjectEntry added = _workspace.AddProject(folder).Project;
        _workspace.Store.Pin(added.Id, new PinnedAction(new ActionRef("build-run.1"), "Build & run", "build-run.ps1"));
        _workspace.Store.SetMain(added.Id, new ActionRef("build-run.1"));
        return _workspace.Store.Find(added.Id)!;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.True(condition(), "the condition did not become true in time");
    }

    [Fact]
    public void AddingAKnownFolderReturnsItsProjectAndAddsNothing()
    {
        string folder = _scratch.Folder("alpha");
        ProjectEntry first = _workspace.AddProject(folder).Project;

        AddResult again = _workspace.AddProject(folder);

        Assert.True(again.AlreadyKnown);
        Assert.Equal(first.Id, again.Project.Id);
        Assert.Single(_workspace.Store.Projects);
    }

    [Fact]
    public async Task AProjectCannotBeRemovedWhileItsActionRuns()
    {
        ProjectEntry project = AddWithMenu("alpha");
        await _workspace.StartMainActionAsync(project.Id);

        RemoveOutcome outcome = _workspace.RemoveProject(project.Id);

        Assert.Equal(RemoveOutcome.RefusedWhileRunning, outcome);
        Assert.NotNull(_workspace.Store.Find(project.Id));
        Assert.NotNull(_workspace.Board.ActiveRun(project.Id));
    }

    [Fact]
    public async Task AProjectWhoseActionEndedCanBeRemovedAndItsStateGoesWithIt()
    {
        ProjectEntry project = AddWithMenu("alpha");
        await _workspace.StartMainActionAsync(project.Id);
        File.WriteAllText(Path.Combine(Directory.GetDirectories(Path.Combine(_workspace.ModuleDirectory, "runs")).Single(), RunWrapper.ResultFile), "1");
        _workspace.Runner.PollOnce();
        await _workspace.Readings.RefreshAsync([project], TestContext.Current.CancellationToken);
        Assert.NotNull(_workspace.Board.LastResult(project.Id, new ActionRef("build-run.1")));
        Assert.NotNull(_workspace.Readings.Get(project.Id));

        RemoveOutcome outcome = _workspace.RemoveProject(project.Id);

        Assert.Equal(RemoveOutcome.Removed, outcome);
        Assert.Null(_workspace.Store.Find(project.Id));
        Assert.Null(_workspace.Board.LastResult(project.Id, new ActionRef("build-run.1")));
        Assert.Null(_workspace.Readings.Get(project.Id));
        Assert.True(Directory.Exists(project.Path));
    }

    [Fact]
    public void RemovingAnUnknownProjectSaysSo()
    {
        Assert.Equal(RemoveOutcome.NotFound, _workspace.RemoveProject("missing"));
    }

    [Fact]
    public async Task AnotherProjectKeepsRunningWhileOneIsRemoved()
    {
        ProjectEntry running = AddWithMenu("alpha");
        ProjectEntry other = AddWithMenu("beta");
        await _workspace.StartMainActionAsync(running.Id);

        Assert.Equal(RemoveOutcome.Removed, _workspace.RemoveProject(other.Id));
        Assert.NotNull(_workspace.Board.ActiveRun(running.Id));
    }

    [Fact]
    public async Task TheMainActionStartsInTheProjectsTerminalAndAProjectWithoutOneIsRefused()
    {
        ProjectEntry project = AddWithMenu("alpha");
        ProjectEntry bare = _workspace.AddProject(_scratch.Folder("bare")).Project;

        StartOutcome started = await _workspace.StartMainActionAsync(project.Id);
        StartOutcome refused = await _workspace.StartMainActionAsync(bare.Id);
        StartOutcome unknown = await _workspace.StartMainActionAsync("missing");

        Assert.True(started.Started);
        Assert.Equal(project.Path, Assert.Single(_launcher.Commands).WorkingDirectory);
        Assert.Equal(StartRefusal.NotPinned, refused.Refusal);
        Assert.Equal(StartRefusal.NotPinned, unknown.Refusal);
    }

    [Fact]
    public async Task APinnedActionStartsByItsReferenceAndOnlyIfPinned()
    {
        ProjectEntry project = AddWithMenu("alpha");
        _workspace.Store.Pin(project.Id, new PinnedAction(new ActionRef("self-check.0", "Debug"), "Self-check", "self-check.ps1"));

        StartOutcome pinned = await _workspace.StartPinnedActionAsync(project.Id, new ActionRef("self-check.0", "Debug"));
        StartOutcome other = await _workspace.StartPinnedActionAsync(project.Id, new ActionRef("not-pinned.0"));

        Assert.True(pinned.Started);
        Assert.Equal("Self-check", pinned.Run!.Label);
        Assert.Equal(StartRefusal.NotPinned, other.Refusal);
    }

    [Fact]
    public async Task AnActionThatEndsBringsTheReadoutsUpToDate()
    {
        ProjectEntry project = AddWithMenu("alpha");
        await _workspace.StartMainActionAsync(project.Id);
        int before = _reader.Reads;

        File.WriteAllText(Path.Combine(Directory.GetDirectories(Path.Combine(_workspace.ModuleDirectory, "runs")).Single(), RunWrapper.ResultFile), "0");
        _workspace.Runner.PollOnce();

        await Eventually(() => _reader.Reads == before + 1);
        await Eventually(() => _workspace.Readings.Get(project.Id) is not null);
    }

    [Fact]
    public async Task ARunThatStartsDoesNotReadTheFolderAgain()
    {
        ProjectEntry project = AddWithMenu("alpha");

        await _workspace.StartMainActionAsync(project.Id);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, _reader.Reads);
    }

    [Fact]
    public async Task AListedMenuBringsThePinsUpToDateAndIsCached()
    {
        ProjectEntry project = AddWithMenu("alpha");
        _menus.Answer = new ScriptMenuListing.Listed([new ScriptMenuEntry("build-run.1", "Build and run", "build-run.ps1", "", [])]);

        ScriptMenuListing first = await _workspace.Menus.GetAsync(project, TestContext.Current.CancellationToken);
        ScriptMenuListing second = await _workspace.Menus.GetAsync(project, TestContext.Current.CancellationToken);

        Assert.IsType<ScriptMenuListing.Listed>(first);
        Assert.IsType<ScriptMenuListing.Listed>(second);
        Assert.Equal(1, _menus.Reads);
        Assert.Equal("Build and run", _workspace.Store.Find(project.Id)!.MainAction!.Label);
    }

    [Fact]
    public async Task AMenuThatIsNotListedIsNotCachedSoTheNextAskReadsAgain()
    {
        ProjectEntry project = AddWithMenu("alpha");
        _menus.Answer = new ScriptMenuListing.NoMenu();

        await _workspace.Menus.GetAsync(project, TestContext.Current.CancellationToken);
        _menus.Answer = new ScriptMenuListing.Listed([new ScriptMenuEntry("build-run.1", "Build & run", "build-run.ps1", "", [])]);
        ScriptMenuListing found = await _workspace.Menus.GetAsync(project, TestContext.Current.CancellationToken);

        Assert.IsType<ScriptMenuListing.Listed>(found);
        Assert.Equal(2, _menus.Reads);
    }

    [Fact]
    public async Task AMenuThatBecomesUnreadableForgetsTheListing()
    {
        ProjectEntry project = AddWithMenu("alpha");
        _menus.Answer = new ScriptMenuListing.Listed([new ScriptMenuEntry("build-run.1", "Build & run", "build-run.ps1", "", [])]);
        await _workspace.Menus.GetAsync(project, TestContext.Current.CancellationToken);

        _menus.Answer = new ScriptMenuListing.Unreadable("boom");
        await _workspace.Menus.RefreshAsync(project, TestContext.Current.CancellationToken);

        Assert.Null(_workspace.Menus.Cached(project.Id));
    }

    [Fact]
    public async Task ACallerLeavingDoesNotCancelTheMenuReadAnotherCallerWaitsOn()
    {
        ProjectEntry project = AddWithMenu("alpha");
        var gated = new GatedMenuReader();
        using var menus = new ScriptMenuCache(gated, _workspace.Store);
        using var leaving = new CancellationTokenSource();

        Task<ScriptMenuListing> first = menus.RefreshAsync(project, leaving.Token);
        Task<ScriptMenuListing> second = menus.RefreshAsync(project, CancellationToken.None);
        leaving.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        gated.Release(new ScriptMenuListing.Listed([new ScriptMenuEntry("build-run.1", "Build & run", "build-run.ps1", "", [])]));

        Assert.IsType<ScriptMenuListing.Listed>(await second);
        Assert.NotNull(menus.Cached(project.Id));
    }

    // Answers when the test says so, and only if its own token was not cancelled.
    private sealed class GatedMenuReader : IScriptMenuReader
    {
        private readonly TaskCompletionSource<ScriptMenuListing> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(ScriptMenuListing listing) => _answer.SetResult(listing);

        public async Task<ScriptMenuListing> ListAsync(string projectPath, CancellationToken cancellation)
        {
            using (cancellation.Register(() => _answer.TrySetCanceled(cancellation)))
                return await _answer.Task;
        }
    }

    [Fact]
    public async Task TheAgentEstimateOfAnUnknownProjectIsNull()
    {
        Assert.Null(await _workspace.EstimateAgentTokensAsync("missing", TestContext.Current.CancellationToken));
    }
}
