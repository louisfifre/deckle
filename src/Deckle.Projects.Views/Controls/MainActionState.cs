namespace Deckle.Projects.Views;

internal enum MainActionStatus
{
    // Nothing to say: the action is ready, or it succeeded last time (the spec
    // draws no mark on a success).
    Idle,

    // An action of the project runs: a progress ring beside the button.
    Running,

    // The main action's last run failed: the warning glyph in the critical
    // colour.
    Failed,
}

// What a card's or a row's main-action button shows right now, worked out from
// the workspace in one place so Home and the category page cannot disagree.
//
// Label is the name the menu gave the action when it was pinned (the store keeps
// the last label seen). Enabled follows the same reasons the engine refuses to
// start for (a missing menu, no shell, an action already running), so a click
// is rarely refused; the refusal stays the safety net.
internal sealed record MainActionState(string Label, bool Enabled, MainActionStatus Status)
{
    // Null when there is nothing to draw: the project has no main action, or its
    // folder is gone (the card says "This folder was not found" and offers no
    // button).
    public static MainActionState? Of(ProjectsWorkspace workspace, ProjectEntry project)
    {
        PinnedAction? main = project.MainAction;
        if (main is null) return null;

        ProjectReading? reading = workspace.Readings.Get(project.Id);
        if (reading?.Folder == FolderState.Missing) return null;

        ActionRun? running = workspace.Board.ActiveRun(project.Id);
        ActionResult? last = workspace.Board.LastResult(project.Id, main.Action);

        // Before the first reading nothing is known against the action, so the
        // button is offered; the engine refuses a start that cannot be made.
        bool menuThere = reading is null || reading.HasScriptMenu;
        IReadOnlyList<ScriptMenuEntry>? listed = workspace.Menus.Cached(project.Id);
        bool menuGivesIt = listed is null || listed.Any(e => e.GivesPin(main));
        bool enabled = running is null && menuThere && menuGivesIt && workspace.Shell is not null;

        MainActionStatus status = running is not null ? MainActionStatus.Running
            : last?.IsFailure == true ? MainActionStatus.Failed
            : MainActionStatus.Idle;
        return new MainActionState(main.Label, enabled, status);
    }
}
