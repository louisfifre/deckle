namespace Deckle.Projects;

// The listings of the projects' script menus, read on demand and kept for the
// window's life. A listing costs a PowerShell start (about 1.25 s, measured),
// so it is read when a project's page opens, when "Pin an action..." needs it
// and when the window comes back to the front on a page that had no menu, and
// never for Home.
//
// Only a successful listing is kept: "no menu", "no shell" and "could not be
// read" are cheap to find out again or worth trying again, and the person who
// just ran the install command expects the page to find the menu when the
// window comes back. Every successful listing also brings the store's pins up to
// date (labels and script names), so Home's card can name the main action
// without reading the menu.
public sealed class ScriptMenuCache(IScriptMenuReader reader, ProjectStore store)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<ScriptMenuEntry>> _listed = [];
    private readonly Dictionary<string, Task<ScriptMenuListing>> _inFlight = [];

    // The entries of the last successful listing, or null when none was read.
    public IReadOnlyList<ScriptMenuEntry>? Cached(string projectId)
    {
        lock (_gate) return _listed.GetValueOrDefault(projectId);
    }

    // The cached listing when there is one, else a fresh read.
    public Task<ScriptMenuListing> GetAsync(ProjectEntry project, CancellationToken cancellation)
    {
        IReadOnlyList<ScriptMenuEntry>? cached = Cached(project.Id);
        return cached is not null
            ? Task.FromResult<ScriptMenuListing>(new ScriptMenuListing.Listed(cached))
            : RefreshAsync(project, cancellation);
    }

    // Reads the menu again whatever is cached. Two reads of one project at once
    // are one read.
    public Task<ScriptMenuListing> RefreshAsync(ProjectEntry project, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (_inFlight.TryGetValue(project.Id, out Task<ScriptMenuListing>? running)) return running;
            Task<ScriptMenuListing> task = Task.Run(() => ReadAsync(project, cancellation), CancellationToken.None);
            _inFlight[project.Id] = task;
            return task;
        }
    }

    // Forgets a project that left the store.
    public void Forget(string projectId)
    {
        lock (_gate) _listed.Remove(projectId);
    }

    private async Task<ScriptMenuListing> ReadAsync(ProjectEntry project, CancellationToken cancellation)
    {
        try
        {
            ScriptMenuListing listing = await reader.ListAsync(project.Path, cancellation).ConfigureAwait(false);
            if (listing is ScriptMenuListing.Listed listed)
            {
                lock (_gate) _listed[project.Id] = listed.Entries;
                store.RefreshPinned(project.Id, listed.Entries);
            }
            else
            {
                // A menu that is no longer readable no longer counts as listed.
                lock (_gate) _listed.Remove(project.Id);
            }
            return listing;
        }
        finally
        {
            lock (_gate) _inFlight.Remove(project.Id);
        }
    }
}
