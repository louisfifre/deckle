using System.Diagnostics;

namespace Deckle.Projects;

// Why an action was not started. The view disables the buttons for the same
// reasons, so a refusal is the safety net behind them.
public enum StartRefusal
{
    FolderMissing,
    NoMenu,
    NoShell,
    AlreadyRunning,

    // The project is unknown, or the action is not one of its pins.
    NotPinned,

    // No terminal could be opened, or the run's folder could not be made.
    TerminalUnavailable,
}

public sealed record StartOutcome(ActionRun? Run, StartRefusal? Refusal)
{
    public bool Started => Run is not null;
}

// Whether the shell that hosts an action's wrapper is still there. The id alone
// is not enough: after the shell is gone Windows may give the number to another
// program, so the process must also carry the shell's name.
public interface IProcessProbe
{
    bool IsAlive(int processId);
}

public sealed class SystemProcessProbe(string? shell) : IProcessProbe
{
    private readonly string? _name = shell is null ? null : Path.GetFileNameWithoutExtension(shell);

    public bool IsAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited) return false;
            return _name is null || string.Equals(process.ProcessName, _name, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // No such process: the shell is gone.
            return false;
        }
    }
}

// Starts an action of a project's script menu in a terminal and follows it to
// its end (spec, Pinned actions).
//
// How it follows what it does not own. wt.exe returns at once whatever the
// command does, so the action runs inside a small wrapper (RunWrapper) that
// writes its own process id to started.txt in a folder of its own, runs
// `menu.ps1 -Run <id> [-Variant <v>]`, then writes the exit code to result.txt.
// Deckle watches that folder every 500 ms, one timer per running action:
//
//   result.txt appears   the action ended with that code (0 Succeeded, else Failed)
//   the shell is gone, no result   the terminal was closed first: Closed
//   no started.txt after 20 s      no terminal reported in: NotStarted
//
// A run's folder is deleted when it ends. At startup, folders older than a day
// are deleted: a Deckle that closed mid-run leaves one, and the person's
// terminal goes on (spec, Open 2, answer A).
//
// Leaving the page or closing the window does not stop an action: it lives in
// its terminal. Dispose only stops watching.
public sealed class ActionRunner : IDisposable
{
    // Nothing reported in by then: the terminal did not start.
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(1);

    // A process-wide counter: it pairs a start and an end in the log, and keeps
    // two runs of one session apart.
    private static long _runCounter;

    private sealed class Watch(string projectId, ActionRun run, string folder)
    {
        public string ProjectId { get; } = projectId;
        public ActionRun Run { get; } = run;
        public string Folder { get; } = folder;
        public int? ProcessId { get; set; }
    }

    private readonly ActionRunBoard _board;
    private readonly ITerminalLauncher _launcher;
    private readonly string _runsFolder;
    private readonly string? _shell;
    private readonly TimeProvider _clock;
    private readonly TerminalWindowFinder _finder;
    private readonly IProcessProbe _probe;
    private readonly bool _watch;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Watch> _watches = [];

    // watch: false leaves the watching to the caller (PollOnce), which is how a
    // test advances a fake clock without a timer.
    public ActionRunner(
        ActionRunBoard board, ITerminalLauncher launcher, string runsFolder, string? shell,
        TimeProvider? clock = null, TerminalWindowFinder? finder = null, IProcessProbe? probe = null, bool watch = true)
    {
        _board = board;
        _launcher = launcher;
        _runsFolder = runsFolder;
        _shell = shell;
        _clock = clock ?? TimeProvider.System;
        _finder = finder ?? new TerminalWindowFinder();
        _probe = probe ?? new SystemProcessProbe(shell);
        _watch = watch;
        DeleteStaleRuns();
    }

    public async Task<StartOutcome> StartAsync(ProjectEntry project, PinnedAction action)
    {
        if (!Directory.Exists(project.Path)) return Refused(StartRefusal.FolderMissing, "folder-missing");

        string menu = Path.Combine(project.Path, "scripts", "menu.ps1");
        if (!File.Exists(menu)) return Refused(StartRefusal.NoMenu, "no-menu");
        if (_shell is null) return Refused(StartRefusal.NoShell, "no-shell");

        long runId = Interlocked.Increment(ref _runCounter);
        string nonce = Random.Shared.Next(0x10000).ToString("x4");
        string title = $"{action.Label} · {project.DisplayName} ({nonce})";
        string folder = Path.Combine(_runsFolder, $"{runId}-{nonce}");
        var run = new ActionRun(runId, action.Action, action.Label, _clock.GetUtcNow(), title);

        if (_board.Begin(project.Id, run) is null) return Refused(StartRefusal.AlreadyRunning, "already-running");

        TerminalStart start;
        try
        {
            Directory.CreateDirectory(folder);
            string script = RunWrapper.Build(new RunWrapperInput(title, folder, _shell, menu, action.Action.Id, action.Action.Variant));
            var command = new TerminalCommand(project.Path, title, _shell, script);
            // Asking for a terminal starts a process: off the caller's thread.
            start = await Task.Run(() => _launcher.Launch(command)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Whatever broke (the run folder, the launcher), the project must
            // not stay marked as running: the start below frees it. The cause
            // is logged, since the person only sees a start that did nothing.
            DeckleProjectsSource.Log.ActionRunStartFailed(ex.GetType().Name);
            start = TerminalStart.Failed("none", "start-failed");
        }

        if (!start.Started)
        {
            _board.Abort(project.Id);
            DeleteFolder(folder);
            return Refused(StartRefusal.TerminalUnavailable, "terminal-unavailable");
        }

        if (start.UsedFallback)
        {
            _board.NoteFallback(project.Id);
            run = run with { UsedFallback = true };
        }
        DeckleProjectsSource.Log.ActionRunStarted(runId, start.Terminal, start.UsedFallback);

        var watch = new Watch(project.Id, run, folder);
        lock (_gate) _watches[project.Id] = watch;
        if (_watch) _ = Task.Run(() => WatchAsync(watch));
        return new StartOutcome(run, null);
    }

    // Brings the terminal of the project's running action to the front. False
    // when no action runs or no window carries its title; the run's own state
    // line stays, nothing more is said.
    //
    // With an action, the window of that action's last run is brought up when
    // it is not the one running (see TerminalIsOpen).
    public bool TryShowTerminal(string projectId, ActionRef? action = null)
    {
        ActionRun? run = _board.ActiveRun(projectId);
        if (run is not null && (action is null || run.Action == action)) return _finder.Show(run.Title);
        return action is not null && EndedTerminalTitle(projectId, action) is { } title && _finder.Show(title);
    }

    // Whether the terminal of an action's last run, one that ended by itself,
    // is still on screen (the wrapper waits for a key before it closes). A run
    // that was closed or never started has no window to show.
    public bool TerminalIsOpen(string projectId, ActionRef action) =>
        EndedTerminalTitle(projectId, action) is { } title && _finder.Exists(title);

    // TerminalIsOpen for several actions of a project from one look at the
    // windows, which walks every top-level window: the caller asks once per
    // refresh, off the UI thread, and shares the answer between its controls.
    // Returns the actions whose terminal is still open.
    public IReadOnlySet<ActionRef> ActionsWithOpenTerminal(string projectId, IEnumerable<ActionRef> actions)
    {
        var titles = new Dictionary<ActionRef, string>();
        foreach (ActionRef action in actions)
            if (EndedTerminalTitle(projectId, action) is { } title) titles[action] = title;
        if (titles.Count == 0) return new HashSet<ActionRef>();

        IReadOnlySet<string> open = _finder.ExistingAmong(titles.Values);
        return titles.Where(t => open.Contains(t.Value)).Select(t => t.Key).ToHashSet();
    }

    private string? EndedTerminalTitle(string projectId, ActionRef action) =>
        _board.LastResult(projectId, action) is { Outcome: RunOutcome.Succeeded or RunOutcome.Failed } result
            ? result.TerminalTitle
            : null;

    // One look at every running action, as the timer does. Returns how many are
    // still running.
    internal int PollOnce()
    {
        Watch[] watches;
        lock (_gate) watches = [.. _watches.Values];
        foreach (Watch watch in watches) Evaluate(watch);
        lock (_gate) return _watches.Count;
    }

    private async Task WatchAsync(Watch watch)
    {
        using var timer = new PeriodicTimer(PollInterval, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                if (Evaluate(watch)) return;
            }
        }
        catch (OperationCanceledException)
        {
            // Deckle's window closed: the action lives on in its terminal.
        }
    }

    // True when the run is over.
    private bool Evaluate(Watch watch)
    {
        string resultFile = Path.Combine(watch.Folder, RunWrapper.ResultFile);
        if (TryReadNumber(resultFile, out int code))
        {
            Finish(watch, code == 0 ? RunOutcome.Succeeded : RunOutcome.Failed, code);
            return true;
        }

        if (watch.ProcessId is null && TryReadNumber(Path.Combine(watch.Folder, RunWrapper.StartedFile), out int pid))
            watch.ProcessId = pid;

        if (watch.ProcessId is int shellId)
        {
            if (_probe.IsAlive(shellId)) return false;

            // The shell may have written its result and left between the two
            // looks above.
            if (TryReadNumber(resultFile, out code))
            {
                Finish(watch, code == 0 ? RunOutcome.Succeeded : RunOutcome.Failed, code);
                return true;
            }
            TimeSpan elapsed = _clock.GetUtcNow() - watch.Run.StartedAt;
            DeckleProjectsSource.Log.ActionRunClosedEarly(watch.Run.RunId, (long)elapsed.TotalMilliseconds);
            Finish(watch, RunOutcome.Closed, null);
            return true;
        }

        if (_clock.GetUtcNow() - watch.Run.StartedAt > StartTimeout)
        {
            Finish(watch, RunOutcome.NotStarted, null);
            return true;
        }
        return false;
    }

    private void Finish(Watch watch, RunOutcome outcome, int? code)
    {
        lock (_gate)
        {
            if (!_watches.Remove(watch.ProjectId)) return;
        }
        DateTimeOffset now = _clock.GetUtcNow();
        _board.End(watch.ProjectId, outcome, code, now);
        DeckleProjectsSource.Log.ActionRunEnded(
            watch.Run.RunId, outcome.ToString(), code ?? -1, (long)(now - watch.Run.StartedAt).TotalMilliseconds);
        DeleteFolder(watch.Folder);
    }

    // A number in a file the wrapper wrote. A file that is not there, is being
    // written or holds something else is "not yet": the next tick tries again.
    private static bool TryReadNumber(string path, out int value)
    {
        value = 0;
        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private StartOutcome Refused(StartRefusal refusal, string reason)
    {
        DeckleProjectsSource.Log.ActionRunNotStarted(reason);
        return new StartOutcome(null, refusal);
    }

    private void DeleteStaleRuns()
    {
        try
        {
            if (!Directory.Exists(_runsFolder)) return;
            DateTime limit = (_clock.GetUtcNow() - StaleAfter).UtcDateTime;
            foreach (string folder in Directory.EnumerateDirectories(_runsFolder))
            {
                if (Directory.GetLastWriteTimeUtc(folder) < limit) DeleteFolder(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed leaves its stale runs for next time.
        }
    }

    private static void DeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => _stop.Cancel();
}
