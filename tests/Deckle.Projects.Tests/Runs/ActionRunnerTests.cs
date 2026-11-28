using Deckle.TestSupport;
using Xunit;

namespace Deckle.Projects.Tests;

// The runner with a terminal that is not one, a clock moved by hand and a probe
// that says which shells are alive: no process starts and no timer ticks. The
// files the wrapper would write are written by the test.
[Collection(ProjectsProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class ActionRunnerTests : IDisposable
{
    private const string Shell = @"C:\fake\pwsh.exe";

    private readonly ScratchFolder _scratch = new();
    private readonly ActionRunBoard _board = new();
    private readonly FakeTerminalLauncher _launcher = new();
    private readonly FakeProcessProbe _probe = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly string _runs;
    private int _projects;

    public ActionRunnerTests()
    {
        _runs = Path.Combine(_scratch.Path, "runs");
    }

    public void Dispose() => _scratch.Dispose();

    private ActionRunner NewRunner(string? shell = Shell, ITerminalLauncher? launcher = null, TerminalWindowFinder? finder = null) =>
        new(_board, launcher ?? _launcher, _runs, shell, _clock, finder, _probe, watch: false);

    private ProjectEntry NewProject(bool withMenu = true, bool folderExists = true)
    {
        string name = $"project{++_projects}";
        string path = Path.Combine(_scratch.Path, name);
        if (folderExists) Directory.CreateDirectory(path);
        if (folderExists && withMenu) _scratch.File("# stub", name, "scripts", "menu.ps1");
        return new ProjectEntry($"id-{name}", path, null, null, [], null, _clock.GetUtcNow());
    }

    private static PinnedAction Pin(string id, string label, string? variant = null) =>
        new(new ActionRef(id, variant), label, "script.ps1");

    // The folder the runner made for the run, where the wrapper writes.
    private string RunFolder() => Directory.GetDirectories(_runs).Single();

    private static void Write(string folder, string file, string text) => File.WriteAllText(Path.Combine(folder, file), text);

    // ── Starting and refusing ──────────────────────────────────────────────

    [Fact]
    public async Task AStartOpensOneTerminalInTheProjectsFolderRunningTheWrapper()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();

        StartOutcome outcome = await runner.StartAsync(project, Pin("build-run.1", "Build & run"));

        Assert.True(outcome.Started);
        TerminalCommand command = Assert.Single(_launcher.Commands);
        Assert.Equal(project.Path, command.WorkingDirectory);
        Assert.Equal(Shell, command.Shell);
        Assert.Contains("-Run 'build-run.1'", command.ScriptText);
        Assert.StartsWith("Build & run \u00B7 project1 (", command.Title);
        Assert.Equal(outcome.Run, _board.ActiveRun(project.Id));
    }

    [Fact]
    public async Task StartingWhileTheProjectRunsIsRefusedAndStartsNoTerminal()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        await runner.StartAsync(project, Pin("build-run.1", "Build & run"));

        StartOutcome second = await runner.StartAsync(project, Pin("self-check.0", "Self-check", "Debug"));

        Assert.False(second.Started);
        Assert.Equal(StartRefusal.AlreadyRunning, second.Refusal);
        Assert.Single(_launcher.Commands);
    }

    [Fact]
    public async Task AnotherProjectStartsFreely()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry first = NewProject();
        ProjectEntry second = NewProject();
        await runner.StartAsync(first, Pin("build-run.1", "Build & run"));

        StartOutcome outcome = await runner.StartAsync(second, Pin("build-run.1", "Build & run"));

        Assert.True(outcome.Started);
        Assert.Equal(2, _launcher.Commands.Count);
    }

    [Fact]
    public async Task AMissingFolderMenuOrShellIsRefusedWithItsReasonAndNoTerminal()
    {
        ProjectEntry gone = NewProject(folderExists: false);
        ProjectEntry noMenu = NewProject(withMenu: false);
        ProjectEntry fine = NewProject();
        using ActionRunner runner = NewRunner();
        using ActionRunner noShell = NewRunner(shell: null);

        Assert.Equal(StartRefusal.FolderMissing, (await runner.StartAsync(gone, Pin("a.0", "A"))).Refusal);
        Assert.Equal(StartRefusal.NoMenu, (await runner.StartAsync(noMenu, Pin("a.0", "A"))).Refusal);
        Assert.Equal(StartRefusal.NoShell, (await noShell.StartAsync(fine, Pin("a.0", "A"))).Refusal);

        Assert.Empty(_launcher.Commands);
        Assert.Null(_board.ActiveRun(fine.Id));
    }

    [Fact]
    public async Task ATerminalThatCannotBeOpenedFreesTheProjectAndLeavesNoRunFolder()
    {
        _launcher.Answer = TerminalStart.Failed(TerminalStart.Console, "start-failed");
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();

        StartOutcome outcome = await runner.StartAsync(project, Pin("a.0", "A"));

        Assert.Equal(StartRefusal.TerminalUnavailable, outcome.Refusal);
        Assert.Null(_board.ActiveRun(project.Id));
        Assert.Empty(Directory.GetDirectories(_runs));
    }

    [Fact]
    public async Task ALauncherThatThrowsWhateverItThrowsFreesTheProject()
    {
        _launcher.Throws = new InvalidOperationException("boom");
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();

        StartOutcome outcome = await runner.StartAsync(project, Pin("a.0", "A"));

        Assert.Equal(StartRefusal.TerminalUnavailable, outcome.Refusal);
        Assert.Null(_board.ActiveRun(project.Id));
        Assert.Empty(Directory.GetDirectories(_runs));
    }

    [Fact]
    public async Task AFallbackToTheConsoleIsKnownOnTheRunAndItsResult()
    {
        _launcher.Answer = new TerminalStart(true, true, TerminalStart.Console);
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();

        StartOutcome outcome = await runner.StartAsync(project, Pin("a.0", "A"));
        Write(RunFolder(), RunWrapper.ResultFile, "0");
        runner.PollOnce();

        Assert.True(outcome.Run!.UsedFallback);
        Assert.True(_board.LastResult(project.Id, new ActionRef("a.0"))!.UsedFallback);
    }

    // ── Following to the end ───────────────────────────────────────────────

    [Fact]
    public async Task AResultOfZeroEndsAsSucceededAndTheFolderGoes()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        await runner.StartAsync(project, Pin("build-run.1", "Build & run"));
        string folder = RunFolder();
        Write(folder, RunWrapper.StartedFile, "4242");
        _probe.Alive.Add(4242);

        Assert.Equal(1, runner.PollOnce());
        Assert.NotNull(_board.ActiveRun(project.Id));

        _clock.Advance(TimeSpan.FromSeconds(30));
        Write(folder, RunWrapper.ResultFile, "0");
        Assert.Equal(0, runner.PollOnce());

        ActionResult result = _board.LastResult(project.Id, new ActionRef("build-run.1"))!;
        Assert.Equal(RunOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(_board.ActiveRun(project.Id));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task ANonZeroResultEndsAsFailedWithItsCode()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        await runner.StartAsync(project, Pin("build-run.1", "Build & run"));
        Write(RunFolder(), RunWrapper.ResultFile, "1\n");

        runner.PollOnce();

        ActionResult result = _board.LastResult(project.Id, new ActionRef("build-run.1"))!;
        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task AHalfWrittenResultIsRetriedOnTheNextTick()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        await runner.StartAsync(project, Pin("a.0", "A"));
        string folder = RunFolder();
        Write(folder, RunWrapper.StartedFile, "4242");
        _probe.Alive.Add(4242);
        Write(folder, RunWrapper.ResultFile, "");

        Assert.Equal(1, runner.PollOnce());

        Write(folder, RunWrapper.ResultFile, "0");
        Assert.Equal(0, runner.PollOnce());
        Assert.Equal(RunOutcome.Succeeded, _board.LastResult(project.Id, new ActionRef("a.0"))!.Outcome);
    }

    [Fact]
    public async Task TheFailureOfOneActionStaysWhileAnotherRunsAndEnds()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        await runner.StartAsync(project, Pin("build-run.1", "Build & run"));
        Write(RunFolder(), RunWrapper.ResultFile, "1");
        runner.PollOnce();

        await runner.StartAsync(project, Pin("self-check.0", "Self-check", "Debug"));
        Write(Directory.GetDirectories(_runs).Single(), RunWrapper.ResultFile, "0");
        runner.PollOnce();

        Assert.Equal(RunOutcome.Failed, _board.LastResult(project.Id, new ActionRef("build-run.1"))!.Outcome);
        Assert.Equal(RunOutcome.Succeeded, _board.LastResult(project.Id, new ActionRef("self-check.0", "Debug"))!.Outcome);
    }

    [Fact]
    public async Task ASuccessStaysUntilThatActionRunsAgain()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        await runner.StartAsync(project, Pin("a.0", "A"));
        Write(RunFolder(), RunWrapper.ResultFile, "0");
        runner.PollOnce();
        _clock.Advance(TimeSpan.FromHours(3));

        Assert.NotNull(_board.LastResult(project.Id, new ActionRef("a.0")));

        await runner.StartAsync(project, Pin("a.0", "A"));
        Assert.Null(_board.LastResult(project.Id, new ActionRef("a.0")));
    }

    [Fact]
    public async Task AShellThatIsGoneWithNoResultIsClosedWithoutAnExitCode()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        await runner.StartAsync(project, Pin("a.0", "A"));
        Write(RunFolder(), RunWrapper.StartedFile, "4242");
        // 4242 is not in the probe's alive set: its shell is gone.

        runner.PollOnce();

        ActionResult result = _board.LastResult(project.Id, new ActionRef("a.0"))!;
        Assert.Equal(RunOutcome.Closed, result.Outcome);
        Assert.Null(result.ExitCode);
        Assert.Null(_board.ActiveRun(project.Id));
    }

    [Fact]
    public async Task NoStartedFileInTwentySecondsIsATerminalThatDidNotStart()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        await runner.StartAsync(project, Pin("a.0", "A"));

        _clock.Advance(TimeSpan.FromSeconds(19));
        Assert.Equal(1, runner.PollOnce());
        Assert.NotNull(_board.ActiveRun(project.Id));

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, runner.PollOnce());

        Assert.Equal(RunOutcome.NotStarted, _board.LastResult(project.Id, new ActionRef("a.0"))!.Outcome);
    }

    [Fact]
    public async Task TheBoardRaisesOneChangePerTransition()
    {
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        var changes = new List<string>();
        _board.Changed += changes.Add;

        await runner.StartAsync(project, Pin("a.0", "A"));
        runner.PollOnce();
        Write(RunFolder(), RunWrapper.ResultFile, "0");
        runner.PollOnce();
        runner.PollOnce();

        Assert.Equal([project.Id, project.Id], changes);
    }

    // ── Showing the terminal ───────────────────────────────────────────────

    [Fact]
    public async Task ShowTerminalBringsTheWindowCarryingTheRunsTitleToTheFront()
    {
        var windows = new FakeWindowSystem();
        using ActionRunner runner = NewRunner(finder: new TerminalWindowFinder(windows));
        ProjectEntry project = NewProject();
        StartOutcome outcome = await runner.StartAsync(project, Pin("a.0", "A"));
        windows.Windows.Add(new TopWindow(7, "Some other window"));
        windows.Windows.Add(new TopWindow(9, outcome.Run!.Title));

        Assert.True(runner.TryShowTerminal(project.Id));
        Assert.Equal(["front 9"], windows.Calls);
    }

    [Fact]
    public void ShowTerminalWithNoRunningActionDoesNothing()
    {
        var windows = new FakeWindowSystem();
        using ActionRunner runner = NewRunner(finder: new TerminalWindowFinder(windows));

        Assert.False(runner.TryShowTerminal("nobody"));
        Assert.Empty(windows.Calls);
    }

    [Fact]
    public async Task AnEndedRunsTerminalIsOfferedWhileItsWindowIsOpenAndBroughtToTheFront()
    {
        var windows = new FakeWindowSystem();
        using ActionRunner runner = NewRunner(finder: new TerminalWindowFinder(windows));
        ProjectEntry project = NewProject();
        PinnedAction pin = Pin("a.0", "A");
        StartOutcome outcome = await runner.StartAsync(project, pin);
        runner.PollOnce();
        Write(RunFolder(), RunWrapper.ResultFile, "1");
        runner.PollOnce();

        Assert.False(runner.TerminalIsOpen(project.Id, pin.Action));

        // The wrapper waits for a key, so the window is still there after the end.
        windows.Windows.Add(new TopWindow(9, outcome.Run!.Title));
        Assert.True(runner.TerminalIsOpen(project.Id, pin.Action));
        Assert.True(runner.TryShowTerminal(project.Id, pin.Action));
        Assert.Equal(["front 9"], windows.Calls);
    }

    [Fact]
    public async Task ARunThatWasClosedLeavesNoTerminalToShow()
    {
        var windows = new FakeWindowSystem();
        using ActionRunner runner = NewRunner(finder: new TerminalWindowFinder(windows));
        ProjectEntry project = NewProject();
        PinnedAction pin = Pin("a.0", "A");
        StartOutcome outcome = await runner.StartAsync(project, pin);
        windows.Windows.Add(new TopWindow(9, outcome.Run!.Title));
        _board.End(project.Id, RunOutcome.Closed, null, _clock.GetUtcNow());

        Assert.False(runner.TerminalIsOpen(project.Id, pin.Action));
    }

    [Fact]
    public async Task TheOpenTerminalsOfSeveralActionsAreFoundFromTheSameWindowList()
    {
        var windows = new FakeWindowSystem();
        using ActionRunner runner = NewRunner(finder: new TerminalWindowFinder(windows));
        ProjectEntry project = NewProject();
        PinnedAction pin = Pin("a.0", "A");
        PinnedAction never = Pin("b.0", "B");
        StartOutcome outcome = await runner.StartAsync(project, pin);
        runner.PollOnce();
        Write(RunFolder(), RunWrapper.ResultFile, "0");
        runner.PollOnce();

        Assert.Empty(runner.ActionsWithOpenTerminal(project.Id, [pin.Action, never.Action]));

        windows.Windows.Add(new TopWindow(9, outcome.Run!.Title));
        Assert.Equal([pin.Action], runner.ActionsWithOpenTerminal(project.Id, [pin.Action, never.Action]));
    }

    // ── The runs folder ────────────────────────────────────────────────────

    [Fact]
    public void RunFoldersOlderThanADayAreDeletedAtStartAndAFreshOneIsKept()
    {
        string stale = Directory.CreateDirectory(Path.Combine(_runs, "1-aaaa")).FullName;
        string fresh = Directory.CreateDirectory(Path.Combine(_runs, "2-bbbb")).FullName;
        Directory.SetLastWriteTimeUtc(stale, _clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(2));
        Directory.SetLastWriteTimeUtc(fresh, _clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(1));

        using ActionRunner runner = NewRunner();

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
    }

    // ── Events ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AStartAndItsEndAreTwoMilestonesPairedByOneRunId()
    {
        using var listener = new TestEventListener("Deckle-Projects");
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();

        StartOutcome outcome = await runner.StartAsync(project, Pin("secret-action.0", "Secret label"));
        _clock.Advance(TimeSpan.FromSeconds(5));
        Write(RunFolder(), RunWrapper.ResultFile, "2");
        runner.PollOnce();

        var started = Assert.Single(listener.Events, e => e.EventId == DeckleProjectsSource.EvtActionRunStarted);
        var ended = Assert.Single(listener.Events, e => e.EventId == DeckleProjectsSource.EvtActionRunEnded);
        Assert.Equal(outcome.Run!.RunId, started.Payload?[0]);
        Assert.Equal(outcome.Run.RunId, ended.Payload?[0]);
        Assert.Equal("Failed", ended.Payload?[1]);
        Assert.Equal(2L, ended.Payload?[2]);
        Assert.Equal(5000L, ended.Payload?[3]);
        Assert.Equal(DeckleProjectsSource.EvtActionRunStarted, started.EventId);
        string[] texts = [.. listener.Events.SelectMany(e => (e.Payload ?? []).OfType<string>().Concat([e.Message ?? ""]))];
        Assert.DoesNotContain(texts, t => t.Contains("secret", StringComparison.OrdinalIgnoreCase) || t.Contains(_scratch.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ARefusalIsOneWarningWithItsReason()
    {
        using var listener = new TestEventListener("Deckle-Projects");
        using ActionRunner runner = NewRunner();
        ProjectEntry gone = NewProject(folderExists: false);

        await runner.StartAsync(gone, Pin("a.0", "A"));

        var warning = Assert.Single(listener.Events, e => e.EventId == DeckleProjectsSource.EvtActionRunNotStarted);
        Assert.Equal("folder-missing", warning.Payload?[0]);
    }

    [Fact]
    public async Task ATerminalClosedEarlyIsAWarningWithTheRunId()
    {
        using var listener = new TestEventListener("Deckle-Projects");
        using ActionRunner runner = NewRunner();
        ProjectEntry project = NewProject();
        StartOutcome outcome = await runner.StartAsync(project, Pin("a.0", "A"));
        Write(RunFolder(), RunWrapper.StartedFile, "4242");

        runner.PollOnce();

        var warning = Assert.Single(listener.Events, e => e.EventId == DeckleProjectsSource.EvtActionRunClosedEarly);
        Assert.Equal(outcome.Run!.RunId, warning.Payload?[0]);
    }
}
