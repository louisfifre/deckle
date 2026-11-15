namespace Deckle.Projects.Views;

internal static class ProjectStoreExtensions
{
    // Nothing at all: no project and no category. This is when Home shows its
    // empty screen (board H0) and the title bar has no pane button. A store with
    // categories and no project is not empty: the person made those categories,
    // and removing the last project must not make them vanish.
    public static bool HoldsNothing(this ProjectStore store) =>
        store.Projects.Count == 0 && store.Categories.Count == 0;
}
