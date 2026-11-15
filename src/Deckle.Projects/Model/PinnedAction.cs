namespace Deckle.Projects;

// An action the person pinned on a project's page. The store keeps the label
// and the script's file name the menu last gave, not only the reference: Home's
// card shows the main action's name without running the menu's listing (a
// PowerShell start, over a second) for every project at every opening. The
// snapshot is refreshed silently whenever a listing is read
// (ProjectStore.RefreshPinned). The menu still decides what exists, so this is
// a last-seen copy for display, never a second source of truth.
public sealed record PinnedAction(ActionRef Action, string Label, string Script);
