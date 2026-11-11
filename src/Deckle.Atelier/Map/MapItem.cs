namespace Deckle.Atelier;

// When an agent puts an item in its context, judged as if the agent starts in
// the folder the map looks at. Which files load when is each tool's own rule;
// MapConventions holds them with their sources.
public enum MapLoading
{
    // No agent reads it as instructions: a script, a standard file, a server.
    NotLoaded,

    // Read when the work needs it: a skill's body once the skill is chosen, a
    // CLAUDE.md of a sub-folder once a file there is opened, a path-scoped rule.
    OnDemand,

    // Read at the start of every session: what "Toujours lu par un LLM" keeps.
    AtSessionStart,
}

// The agents whose rule makes an item load at session start. An item can be
// always read by one tool and not by another: AGENTS.md is read by Codex at
// every start, and by Claude Code only when no CLAUDE.md stands at the root.
[Flags]
public enum MapReaders
{
    None = 0,
    ClaudeCode = 1,
    Codex = 2,
    GitHubCopilot = 4,
    GeminiCli = 8,
    Cursor = 16,
}

// One thing found in the folder. Id is the relative path, with "/" between
// segments; a server adds "#" and its name to the path of the file that
// declares it, since several servers share one file.
public sealed record MapItem
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required MapItemKind Kind { get; init; }

    // The file that holds the item: SKILL.md for a skill, the configuration
    // file for a server.
    public required string Path { get; init; }

    public required string RelativePath { get; init; }

    // The line of the configuration file where a server is declared; null for
    // every other kind, whose place is the whole file.
    public int? DeclaredAtLine { get; init; }

    // Null for a server, which is a few lines of a shared file and not a file
    // of its own, and for a file too large to read.
    public int? Lines { get; init; }

    // An estimate, never a count: see TokenEstimate.
    public int? EstimatedTokens { get; init; }

    public required DateTime Modified { get; init; }

    public required MapLoading Loading { get; init; }

    public MapReaders ReadAtStartBy { get; init; }

    // What the item puts in the context at session start: the whole file when
    // it is always read; only its name and description for a skill or a
    // subagent, whose body waits until it is chosen; null when nothing loads.
    public int? EstimatedStartupTokens { get; init; }

    // The description the file gives of itself (a skill's front matter), or
    // the command that starts a server. Never written by a model.
    public string? Summary { get; init; }

    public bool IsAlwaysRead => Loading == MapLoading.AtSessionStart;
}
