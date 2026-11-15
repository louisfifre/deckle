namespace Deckle.Projects.Views;

// Why a page is asked to read again what it shows.
internal enum RefreshReason
{
    // F5 (spec, Window: one Refresh command).
    Requested,

    // The window came back to the front: a project page that had no script menu
    // looks for it again, because the person may have just run the install
    // command in a terminal (spec, Pinned actions).
    WindowActivated,
}

// A place the window shows: Home, a category's page, a project's page. The
// window builds one per navigation and disposes it when the person leaves, so
// whatever a page started (a reading, a timer, a subscription) ends with it.
internal interface IProjectsPage : IDisposable
{
    // Reads again what the page shows. Returns when the new values are on
    // screen, which is what the self-check waits for before it takes an image.
    Task RefreshAsync(RefreshReason reason);
}
