namespace Deckle.Projects;

public enum FolderState
{
    Present,

    // The directory is gone (or is a file now): the reading carries nothing
    // else, and the card and the page say "This folder was not found".
    Missing,
}

// What Deckle could tell of a folder's Git state.
public enum RepositoryState
{
    Repository,

    // The folder is fine and is not in a Git repository: "Uncommitted" reads
    // "Not a Git repository".
    NotARepository,

    // Git could not answer (not installed, timed out, dubious ownership): the
    // page shows nothing for git, never an error.
    Unreadable,
}

// What Home's cards and the project page read from a project's folder, all
// found without a model (spec, Behaviours). The token estimate of "An agent
// always reads" is not here: it walks the whole folder, so it is read once
// per page opening by AgentReading and never for Home.
public sealed record ProjectReading(
    FolderState Folder,
    RepositoryState Git,
    VersionReading Version,
    int? UncommittedFiles,
    GitRemote? Remote,
    bool HasScriptMenu,
    DateTimeOffset ReadAt,
    bool GitNotInstalled = false)
{
    public bool IsGitRepository => Git == RepositoryState.Repository;

    public static ProjectReading Missing(DateTimeOffset readAt) =>
        new(FolderState.Missing, RepositoryState.Unreadable, VersionReading.None, null, null, false, readAt);
}
