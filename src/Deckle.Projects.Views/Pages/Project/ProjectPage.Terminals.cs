namespace Deckle.Projects.Views;

// Which ended runs still have their terminal window open, for the small "Show
// terminal" buttons. Finding out walks every top-level window of the desktop, so
// it is never done where the page draws (the one-second tick included): one
// snapshot is taken off the UI thread, shared by the header and the cards, and
// the drawing reads it.
//
// The snapshot is taken again when the board moves, when the window comes back
// to the front, after a "Show terminal" click, and, while some button is shown,
// every few seconds (the person may close the terminal window by hand, and
// nothing tells Deckle).
internal sealed partial class ProjectPage
{
    private static readonly TimeSpan TerminalRecheck = TimeSpan.FromSeconds(4);

    private IReadOnlySet<ActionRef> _openTerminals = new HashSet<ActionRef>();
    private DateTimeOffset _terminalsTaken = DateTimeOffset.MinValue;
    private bool _terminalsBusy;
    private bool _terminalsAgain;

    // Whether the action's last run ended and its window was open at the last
    // snapshot.
    private bool TerminalIsOpen(ActionRef action) => _openTerminals.Contains(action);

    // Called by the tick: takes a new snapshot only when a button is shown and
    // the last one is a few seconds old. The tick itself does nothing else.
    private void RecheckTerminalsIfStale()
    {
        if (_openTerminals.Count == 0) return;
        if (_context.Workspace.Clock.GetUtcNow() - _terminalsTaken < TerminalRecheck) return;
        RefreshTerminals();
    }

    // Takes a snapshot off the UI thread and draws again if it changed. A
    // request made while one is in flight is remembered and served right after,
    // since the snapshot in flight may predate what the request is about.
    private void RefreshTerminals()
    {
        if (_disposed || Project is not { } project) return;
        if (_terminalsBusy)
        {
            _terminalsAgain = true;
            return;
        }

        ActionRef[] actions = [.. project.Pinned.Select(p => p.Action)];
        ProjectsContext context = _context;
        string projectId = _projectId;
        _terminalsBusy = true;

        _ = Task.Run(() =>
        {
            try
            {
                IReadOnlySet<ActionRef> open = context.Workspace.Runner.ActionsWithOpenTerminal(projectId, actions);
                DateTimeOffset taken = context.Workspace.Clock.GetUtcNow();
                context.Dispatch(() =>
                {
                    if (!Finish()) return;
                    _terminalsTaken = taken;
                    if (_openTerminals.SetEquals(open)) return;
                    _openTerminals = open;
                    ApplyActions();
                });
            }
            catch (Exception ex)
            {
                // A window list that cannot be read leaves the buttons as they
                // were; the log says what kind of failure it was.
                DeckleProjectsSource.Log.ProjectsPageFailed("project", ex.GetType().Name);
                context.Dispatch(() => Finish());
            }
        });
    }

    // The snapshot in flight is over. False when the page is gone; otherwise a
    // request that came meanwhile starts its own snapshot.
    private bool Finish()
    {
        _terminalsBusy = false;
        if (_disposed) return false;
        if (_terminalsAgain)
        {
            _terminalsAgain = false;
            RefreshTerminals();
        }
        return true;
    }
}
