using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class AppSessionTrackerTests
{
    private const string GameExe = @"C:\Games\Hollow\Hollow.exe";

    private readonly List<AppEntry> _entries = [];
    private readonly List<(AppEntry Entry, int Pid)> _started = [];
    private readonly List<AppEntry> _ended = [];
    private readonly AppSessionTracker _tracker;

    public AppSessionTrackerTests()
    {
        _tracker = new AppSessionTracker(() => _entries);
        _tracker.SessionStarted += (entry, pid) => _started.Add((entry, pid));
        _tracker.SessionEnded += _ended.Add;
    }

    [Fact]
    public void SeenProcessOfAnEnabledEntryStartsOneSession()
    {
        AppEntry game = Add(Game());

        _tracker.OnProcessSeen(100, "Hollow", GameExe);

        var started = Assert.Single(_started);
        Assert.Same(game, started.Entry);
        Assert.Equal(100, started.Pid);
        var session = Assert.Single(_tracker.Sessions);
        Assert.Equal([100], session.ProcessIds);
    }

    [Fact]
    public void SecondProcessOfTheSameEntryStartsNoSecondSession()
    {
        Add(Game());

        _tracker.OnProcessSeen(100, "Hollow", GameExe);
        _tracker.OnProcessSeen(101, "Hollow", GameExe);
        _tracker.OnProcessSeen(100, "Hollow", GameExe);

        Assert.Single(_started);
        var session = Assert.Single(_tracker.Sessions);
        Assert.Equal([100, 101], session.ProcessIds.Order());
    }

    [Fact]
    public void SessionEndsOnlyWhenItsLastProcessExits()
    {
        AppEntry game = Add(Game());
        _tracker.OnProcessSeen(100, "Hollow", GameExe);
        _tracker.OnProcessSeen(101, "Hollow", GameExe);

        _tracker.OnProcessExited(100);
        Assert.Empty(_ended);

        _tracker.OnProcessExited(101);
        Assert.Same(game, Assert.Single(_ended));
        Assert.Empty(_tracker.Sessions);
    }

    [Fact]
    public void EveryTrackedProcessIsReported()
    {
        Add(Game());
        var tracked = new List<int>();
        _tracker.ProcessTracked += (_, pid) => tracked.Add(pid);

        _tracker.OnProcessSeen(100, "Hollow", GameExe);
        _tracker.OnProcessSeen(101, "Hollow", GameExe);

        Assert.Equal([100, 101], tracked);
    }

    [Fact]
    public void DisabledEntryNeverStartsASession()
    {
        Add(Game(enabled: false));

        _tracker.OnProcessSeen(100, "Hollow", GameExe);

        Assert.Empty(_started);
        Assert.Empty(_tracker.Sessions);
    }

    [Fact]
    public void DisablingAnEntryEndsItsSession()
    {
        AppEntry game = Add(Game());
        _tracker.OnProcessSeen(100, "Hollow", GameExe);

        game.Enabled = false;
        _tracker.DropSessionsNotIn(_entries);

        Assert.Same(game, Assert.Single(_ended));
        Assert.Empty(_tracker.Sessions);
    }

    [Fact]
    public void RemovingAnEntryEndsItsSessionAndLaterExitsAreIgnored()
    {
        AppEntry game = Add(Game());
        _tracker.OnProcessSeen(100, "Hollow", GameExe);

        _entries.Remove(game);
        _tracker.DropSessionsNotIn(_entries);
        _tracker.OnProcessExited(100);

        Assert.Same(game, Assert.Single(_ended));
    }

    [Fact]
    public void SessionOfAnEntryStillListedSurvivesDrop()
    {
        Add(Game());
        _tracker.OnProcessSeen(100, "Hollow", GameExe);

        _tracker.DropSessionsNotIn(_entries);

        Assert.Empty(_ended);
        Assert.Single(_tracker.Sessions);
    }

    [Fact]
    public void UnmatchedProcessIsIgnored()
    {
        Add(Game());

        _tracker.OnProcessSeen(200, "notepad", @"C:\Windows\notepad.exe");
        _tracker.OnProcessExited(200);

        Assert.Empty(_started);
        Assert.Empty(_ended);
        Assert.Empty(_tracker.Sessions);
    }

    // A handler that reports the exit while the start is being delivered (the
    // exit watch finding the process already gone) sees the end only after
    // its start handler has returned, never nested inside it.
    [Fact]
    public void ExitReportedDuringStartIsDeliveredAfterTheStart()
    {
        Add(Game());
        var order = new List<string>();
        var tracker = new AppSessionTracker(() => _entries);
        tracker.SessionStarted += (_, pid) =>
        {
            order.Add("start-begin");
            tracker.OnProcessExited(pid);
            order.Add("start-end");
        };
        tracker.SessionEnded += _ => order.Add("end");

        tracker.OnProcessSeen(100, "Hollow", GameExe);

        Assert.Equal(["start-begin", "start-end", "end"], order);
    }

    private AppEntry Add(AppEntry entry)
    {
        _entries.Add(entry);
        return entry;
    }

    private static AppEntry Game(bool enabled = true) => new()
    {
        DisplayName = "Hollow",
        Kind = AppKind.Game,
        ExecutablePath = GameExe,
        ProcessName = "Hollow",
        Enabled = enabled,
    };
}
