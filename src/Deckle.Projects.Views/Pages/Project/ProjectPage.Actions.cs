using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// The pinned actions: the cards, "Pin an action...", and what a button starts
// (spec, Pinned actions). Deckle never runs an action by itself: every run starts
// with the person's click on that action, and Deckle adds no confirmation of its
// own, what the menu asks it asks in the terminal.
internal sealed partial class ProjectPage
{
    private void ConfigureActions()
    {
        Pinned.RunRequested += action =>
            _ = StartAsync(() => _context.Workspace.StartPinnedActionAsync(_projectId, action));
        Pinned.ShowTerminalRequested += ShowTerminal;
        Pinned.PinRequested += ShowPinFlyout;
    }

    // Whether the script menu is there: the reading knows, and before the first
    // reading the probe does (false while neither has answered).
    private bool HasScriptMenu(ProjectEntry project) =>
        _context.Workspace.Readings.Get(project.Id)?.HasScriptMenu ?? _probe?.HasMenu ?? false;

    // The same reasons the engine refuses to start for (a folder that is gone, a
    // missing menu, no shell); the engine stays the safety net behind a button
    // that is wrongly on.
    private bool CanStartAnAction(ProjectEntry project, bool missing) =>
        !missing && _context.Workspace.Shell is not null && HasScriptMenu(project);

    private void ApplyPinned(ProjectEntry project)
    {
        bool missing = FolderIsMissing(project);
        PinnedMenuKind kind = MenuKindOf(project);

        // The section appears once, in its final state, when the menu has
        // answered: showing "Pin an action..." first and taking it away when
        // the menu turns out missing would make the page jump. The wait is a
        // local read, short enough to need no ring.
        bool awaitingMenu = kind == PinnedMenuKind.Listed && _listing is null;
        Pinned.Visibility = awaitingMenu ? Visibility.Collapsed : Visibility.Visible;
        if (awaitingMenu) return;

        bool canStart = CanStartAnAction(project, missing) && kind is PinnedMenuKind.Listed or PinnedMenuKind.Unreadable;
        DateTimeOffset now = _context.Workspace.Clock.GetUtcNow();
        ActionRunBoard board = _context.Workspace.Board;
        ActionRun? running = board.ActiveRun(project.Id);
        IReadOnlyList<ScriptMenuEntry>? entries = (_listing as ScriptMenuListing.Listed)?.Entries;

        // The main action is in the header and nowhere else on the page.
        var cards = new List<PinnedCardModel>();
        foreach (PinnedAction pin in project.Pinned)
        {
            if (pin.Action == project.Main) continue;

            // Until the menu has answered nothing is known against the pin, so
            // the card is offered; the engine refuses a start that cannot be made.
            bool menuGivesIt = entries is null || entries.Any(e => e.GivesPin(pin));
            PinnedCardState state = ActionStates.ForCard(
                pin, running, board.LastResult(project.Id, pin.Action), menuGivesIt, canStart, now);

            string detail = pin.Action.Variant is { } variant ? $"{pin.Script} · {variant}" : pin.Script;
            bool terminalOpen = running is null && TerminalIsOpen(pin.Action);
            cards.Add(new PinnedCardModel(pin.Action, pin.Label, detail, state, terminalOpen));
        }

        Pinned.Apply(new PinnedSectionModel(kind, (_listing as ScriptMenuListing.Unreadable)?.LastLines, cards));
    }

    private PinnedMenuKind MenuKindOf(ProjectEntry project)
    {
        if (!HasScriptMenu(project)) return PinnedMenuKind.NoMenu;
        if (_context.Workspace.Shell is null) return PinnedMenuKind.NoShell;

        return _listing switch
        {
            ScriptMenuListing.NoMenu => PinnedMenuKind.NoMenu,
            ScriptMenuListing.NoShell => PinnedMenuKind.NoShell,
            ScriptMenuListing.Unreadable => PinnedMenuKind.Unreadable,
            _ => PinnedMenuKind.Listed,
        };
    }

    // A start the engine refused leaves the page as it was: the buttons are
    // already off for the same reasons, and the refusal is logged by the engine.
    private async Task StartAsync(Func<Task<StartOutcome>> start)
    {
        try
        {
            await start();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DeckleProjectsSource.Log.ProjectsPageFailed("project", ex.GetType().Name);
        }
    }

    // Brings the terminal of the action to the front: the one it runs in, or the
    // one a run that just ended left open. Nothing is said when no window is
    // found: the action's own state line stays, and the line is drawn again so
    // an offer of a window that is gone goes away.
    private void ShowTerminal(ActionRef action)
    {
        _context.Workspace.Runner.TryShowTerminal(_projectId, action);
        RefreshTerminals();
        ApplyActions();
    }

    // The list of the menu's entries, under "Pin an action...". Opens at once; when
    // the menu has not answered yet, the list shows a progress ring and fills.
    private void ShowPinFlyout()
    {
        if (Project is not { } project) return;

        Microsoft.UI.Xaml.Controls.Flyout flyout = PinActionFlyout.Build(
            _context, project.Id, _listing, () => ReadListingForFlyoutAsync(project));
        flyout.ShowAt(Pinned.PinButton);
    }

    private async Task<ScriptMenuListing> ReadListingForFlyoutAsync(ProjectEntry project)
    {
        ScriptMenuListing listing = await _context.Workspace.Menus.GetAsync(project, _lifetime.Token);
        _listing = listing;
        if (!_disposed) Apply();
        return listing;
    }
}
