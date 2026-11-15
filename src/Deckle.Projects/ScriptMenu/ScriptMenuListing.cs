namespace Deckle.Projects;

// What reading a project's script menu came to. The page shows one of four
// things, so the answer is one of four shapes rather than a value and a flag.
public abstract record ScriptMenuListing
{
    private ScriptMenuListing() { }

    // The project has no scripts\menu.ps1 (P2): the page offers to copy the
    // install command.
    public sealed record NoMenu : ScriptMenuListing;

    // Neither pwsh nor powershell is on the machine: nothing can be listed or
    // run, and the page says so.
    public sealed record NoShell : ScriptMenuListing;

    // The menu exists and could not be read: it failed, took too long or printed
    // nothing that reads as an entry. LastLines are the error's last lines, shown
    // on the page and never written to a log (they are the project's own text).
    public sealed record Unreadable(string LastLines) : ScriptMenuListing;

    public sealed record Listed(IReadOnlyList<ScriptMenuEntry> Entries) : ScriptMenuListing;
}
