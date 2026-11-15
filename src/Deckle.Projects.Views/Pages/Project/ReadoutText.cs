namespace Deckle.Projects.Views;

// The words of the readouts, worked out from a reading. Apart from the page so
// that each rule reads in one place:
//
//   Version       the highest tag ("v0.131.0"), "No version yet" without one
//                 (a project whose manifests declare a version still reads so,
//                 and its info button lists what they say)
//   Uncommitted   "3 files", "1 file", "Nothing", "Not a Git repository"
//   An agent...   "≈ 1,880 tokens", the thousands separator of en-US
//
// A value Deckle could not find out reads "—": git that did not answer is shown
// as nothing known, never as an error (spec, Limits).
internal static class ReadoutText
{
    public static ReadoutsModel Of(ProjectReading? reading, AgentEstimate? agent, bool agentRead)
    {
        string unknown = ProjectsText.Get("Project_Unknown");

        string version = reading is null ? "" : reading.Version.Tag ?? ProjectsText.Get("Project_NoVersion");
        VersionReading? detail = reading is { } r && r.Version.ShowsDetail ? r.Version : null;

        string uncommitted = reading switch
        {
            null => "",
            { Git: RepositoryState.NotARepository } => ProjectsText.Get("Project_NotARepository"),
            { Git: RepositoryState.Unreadable } => unknown,
            { UncommittedFiles: null } => unknown,
            { UncommittedFiles: 0 } => ProjectsText.Get("Project_Nothing"),
            { UncommittedFiles: 1 } => ProjectsText.Get("Project_FilesOne"),
            { UncommittedFiles: int count } => ProjectsText.Format("Project_FilesMany_Format", count),
        };

        // Before the first estimate arrives the value is empty; an estimate that
        // failed reads "—". A truncated map is a floor, and the tooltip says so.
        string tokens = agent is not null
            ? ProjectsText.Format("Project_Tokens_Format", agent.Tokens)
            : agentRead ? unknown : "";
        string? tooltip = agent?.IsTruncated == true ? ProjectsText.Get("Project_TokensFloor") : null;

        return new ReadoutsModel(version, detail, uncommitted, tokens, tooltip);
    }
}
