namespace Deckle.Projects;

// What the host hands the window to reach the other windows of Deckle, so
// Deckle Projects never learns the types of its neighbours (the pattern the
// Folders's photo-description settings hook already uses). A hook that is null
// means the neighbour is not there: its button is absent from the page.
//
// OpenInAgents joins when Deckle Agents exists; the spec says its button is not
// shown before.
public sealed class ProjectsHooks
{
    // Shows the folder in Deckle Folders, adding it there first when Deckle
    // Folders does not hold it yet.
    public Func<string, Task>? OpenInFolders { get; init; }
}
