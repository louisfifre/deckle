using System.Collections.Frozen;

namespace Deckle.Atelier;

// How the map reads a file at a place.
internal readonly record struct MapReading(MapLoading Loading, MapReaders AtStartBy)
{
    public static MapReading NotLoaded => new(MapLoading.NotLoaded, MapReaders.None);

    public static MapReading OnDemand => new(MapLoading.OnDemand, MapReaders.None);

    // Read at every start by these agents; by none, the file is not loaded.
    public static MapReading AtStart(MapReaders readers) =>
        readers == MapReaders.None ? NotLoaded : new(MapLoading.AtSessionStart, readers);
}

// One way of recognising a file: its kind, the test on its place and name,
// how agents load it, and the documentation the rule comes from.
// ExpandsImports marks the instruction files whose @path lines Claude Code or
// Gemini CLI expand.
internal sealed record FileConvention(
    MapItemKind Kind,
    Func<MapFileFacts, bool> Matches,
    Func<MapFileFacts, MapReading> Reading,
    string Source,
    bool ExpandsImports = false);

// An MCP configuration file: its place and the JSON key that holds its servers.
internal sealed record McpConfigConvention(string RelativePath, string ServersKey, string Source);

// Every rule by which the map recognises a thing, in one place, each with the
// documentation it was read from (2026-11-10). The rules are judged as if an
// agent starts in the folder the map looks at: what an ancestor folder holds
// is outside the map. Files are tried in order and the first rule that
// matches wins, so a specific place comes before a name found anywhere.
internal static class MapConventions
{
    internal const string ClaudeMemory = "https://code.claude.com/docs/en/memory";
    internal const string ClaudeSkills = "https://code.claude.com/docs/en/skills";
    internal const string ClaudeSubagents = "https://code.claude.com/docs/en/sub-agents";
    internal const string ClaudeMcp = "https://code.claude.com/docs/en/mcp";
    internal const string AgentsMd = "https://agents.md";
    internal const string CodexAgentsMd = "https://learn.chatgpt.com/docs/agent-configuration/agents-md";
    internal const string CodexSkills = "https://learn.chatgpt.com/docs/build-skills";
    internal const string CopilotInstructions = "https://docs.github.com/en/copilot/how-tos/configure-custom-instructions/add-repository-instructions";
    internal const string GeminiContext = "https://google-gemini.github.io/gemini-cli/docs/cli/gemini-md.html";
    internal const string GeminiMcp = "https://google-gemini.github.io/gemini-cli/docs/tools/mcp-server.html";
    internal const string CursorRules = "https://cursor.com/docs/context/rules";
    internal const string CursorMcp = "https://cursor.com/docs/context/mcp";
    internal const string VsCodeMcp = "https://code.visualstudio.com/docs/copilot/customization/mcp-servers";
    internal const string GitHubHealthFiles = "https://docs.github.com/en/communities/setting-up-your-project-for-healthy-contributions/creating-a-default-community-health-file";
    internal const string GitHubReadme = "https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes";
    internal const string ScriptsByExtension = "Windows runs .cmd and .bat (PATHEXT), PowerShell .ps1, a Unix shell .sh; a Python or JavaScript file counts only when written to be run (a #! line, or Python's __main__ guard)";

    // Claude Code: "Imported files can recursively import other files, with a
    // maximum depth of four hops." Gemini CLI's documentation gives no depth;
    // the same bound is used for both.
    internal const int MaxImportHops = 4;

    // The agents that expand @path in their instruction files.
    internal const MapReaders ImportExpanding = MapReaders.ClaudeCode | MapReaders.GeminiCli;

    // "CLAUDE.md and CLAUDE.local.md files in the directory hierarchy above the
    // working directory are loaded at launch. Files in subdirectories load on
    // demand." These three also make Claude Code skip AGENTS.md.
    private static readonly string[] ClaudeMdAtRoot = ["CLAUDE.md", ".claude/CLAUDE.md", "CLAUDE.local.md"];

    private static bool HasClaudeMdAtRoot(MapFileFacts f) => ClaudeMdAtRoot.Any(f.Exists);

    // Files GitHub recognises at the root, in .github or in docs, plus the
    // changelog, code owners and citation files GitHub reads in the same places.
    private static readonly FrozenSet<string> StandardStems = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "CONTRIBUTING", "CODE_OF_CONDUCT", "SECURITY", "SUPPORT", "ACCESSIBILITY", "FUNDING",
        "LICENSE", "LICENCE", "COPYING", "CHANGELOG", "CODEOWNERS", "CITATION");

    private static readonly string[] StandardFolders = ["", ".github", "docs"];

    private static readonly FrozenSet<string> RunExtensions = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        ".ps1", ".cmd", ".bat", ".sh", ".bash", ".zsh");

    private static readonly FrozenSet<string> RunWhenWrittenToRunExtensions = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        ".py", ".js", ".mjs", ".cjs", ".ts");

    internal static readonly IReadOnlyList<FileConvention> Files =
    [
        // Claude Code reads these three at every start, and expands their imports.
        new(MapItemKind.ContextFile, f => ClaudeMdAtRoot.Any(f.Is),
            _ => MapReading.AtStart(MapReaders.ClaudeCode), ClaudeMemory, ExpandsImports: true),

        // A CLAUDE.md of a sub-folder loads once Claude opens a file there.
        new(MapItemKind.ContextFile, f => f.Named("CLAUDE.md") || f.Named("CLAUDE.local.md"),
            _ => MapReading.OnDemand, ClaudeMemory, ExpandsImports: true),

        // "Rules without paths frontmatter are loaded at launch with the same
        // priority as .claude/CLAUDE.md"; with paths, when matching files are
        // worked on. A sub-folder's .claude/rules loads on demand.
        new(MapItemKind.ContextFile, f => f.Under(".claude/rules") && f.Extension == ".md",
            f => f.FrontMatter.Has("paths") ? MapReading.OnDemand : MapReading.AtStart(MapReaders.ClaudeCode),
            ClaudeMemory, ExpandsImports: true),
        new(MapItemKind.ContextFile, f => f.UnderAny(".claude/rules") && f.Extension == ".md",
            _ => MapReading.OnDemand, ClaudeMemory, ExpandsImports: true),

        // Codex reads one file per folder from the root down, AGENTS.override.md
        // in place of AGENTS.md. Copilot reads "the nearest AGENTS.md"; Cursor
        // points plain Markdown instructions to AGENTS.md. Claude Code reads it
        // "only when you have no CLAUDE.md in your working directory or above
        // it", and expands its @path imports.
        new(MapItemKind.ContextFile, f => f.Is("AGENTS.md"),
            f => MapReading.AtStart(
                (f.Exists("AGENTS.override.md") ? MapReaders.None : MapReaders.Codex)
                | MapReaders.GitHubCopilot | MapReaders.Cursor
                | (HasClaudeMdAtRoot(f) ? MapReaders.None : MapReaders.ClaudeCode)),
            CodexAgentsMd + " ; " + ClaudeMemory + " ; " + CopilotInstructions + " ; " + CursorRules, ExpandsImports: true),
        new(MapItemKind.ContextFile, f => f.Is(".claude/AGENTS.md"),
            f => MapReading.AtStart(HasClaudeMdAtRoot(f) ? MapReaders.None : MapReaders.ClaudeCode),
            ClaudeMemory, ExpandsImports: true),
        new(MapItemKind.ContextFile, f => f.Is("AGENTS.override.md"),
            _ => MapReading.AtStart(MapReaders.Codex), CodexAgentsMd),
        new(MapItemKind.ContextFile, f => f.Named("AGENTS.md") || f.Named("AGENTS.override.md"),
            _ => MapReading.OnDemand, CodexAgentsMd + " ; " + AgentsMd, ExpandsImports: true),

        // Gemini CLI loads GEMINI.md from the folder and its ancestors, and from
        // the sub-folders below it, at start.
        new(MapItemKind.ContextFile, f => f.Named("GEMINI.md"),
            _ => MapReading.AtStart(MapReaders.GeminiCli), GeminiContext, ExpandsImports: true),

        // Copilot: repository instructions "apply to all requests"; a path file
        // applies to the files its applyTo glob names, all of them for "**".
        new(MapItemKind.ContextFile, f => f.Is(".github/copilot-instructions.md"),
            _ => MapReading.AtStart(MapReaders.GitHubCopilot), CopilotInstructions),
        new(MapItemKind.ContextFile,
            f => f.Under(".github/instructions") && f.FileName.EndsWith(".instructions.md", StringComparison.OrdinalIgnoreCase),
            f => f.FrontMatter["applyTo"] is "**" or "**/*" ? MapReading.AtStart(MapReaders.GitHubCopilot) : MapReading.OnDemand,
            CopilotInstructions),

        // Cursor: project rules "must use the .mdc extension"; alwaysApply
        // includes one in every chat, otherwise it attaches by glob or description.
        new(MapItemKind.ContextFile, f => f.Under(".cursor/rules") && f.Extension == ".mdc",
            f => string.Equals(f.FrontMatter["alwaysApply"], "true", StringComparison.OrdinalIgnoreCase)
                ? MapReading.AtStart(MapReaders.Cursor) : MapReading.OnDemand,
            CursorRules),

        // A subagent's description is always in context; its body "only loads
        // when that subagent runs".
        new(MapItemKind.ContextFile, f => f.Under(".claude/agents") && f.Extension == ".md",
            _ => MapReading.OnDemand, ClaudeSubagents),

        // A skill is a folder whose SKILL.md opens with a name and a
        // description: those load at start, the body when the skill is chosen.
        // Claude Code looks in .claude/skills, Codex in .agents/skills, and the
        // format is the same everywhere, so SKILL.md is recognised in any folder.
        new(MapItemKind.Skill, f => f.Named("SKILL.md"),
            _ => MapReading.OnDemand, ClaudeSkills + " ; " + CodexSkills),

        // "Custom commands have been merged into skills."
        new(MapItemKind.Skill, f => f.Under(".claude/commands") && f.Extension == ".md",
            _ => MapReading.OnDemand, ClaudeSkills),

        // GitHub shows a README in .github, at the root or in docs as the
        // repository's, and a folder's own README when that folder is browsed;
        // every README is kept.
        new(MapItemKind.StandardFile, f => string.Equals(f.Stem, "README", StringComparison.OrdinalIgnoreCase),
            _ => MapReading.NotLoaded, GitHubReadme),
        new(MapItemKind.StandardFile,
            f => StandardStems.Contains(f.Stem) && StandardFolders.Any(d => string.Equals(d, f.Folder, StringComparison.OrdinalIgnoreCase)),
            _ => MapReading.NotLoaded, GitHubHealthFiles),

        new(MapItemKind.Script, f => RunExtensions.Contains(f.Extension),
            _ => MapReading.NotLoaded, ScriptsByExtension),
        new(MapItemKind.Script, f => RunWhenWrittenToRunExtensions.Contains(f.Extension) && IsWrittenToRun(f),
            _ => MapReading.NotLoaded, ScriptsByExtension),
    ];

    internal static readonly IReadOnlyList<McpConfigConvention> McpConfigs =
    [
        new(".mcp.json", "mcpServers", ClaudeMcp),
        new(".vscode/mcp.json", "servers", VsCodeMcp),
        new(".cursor/mcp.json", "mcpServers", CursorMcp),
        new(".gemini/settings.json", "mcpServers", GeminiMcp),
    ];

    // Folders the walk does not enter, by name at any depth. "build" and
    // "packages" stay walked: a repository often keeps scripts there.
    internal static readonly FrozenDictionary<string, MapSkipReason> SkippedFolders =
        new Dictionary<string, MapSkipReason>(StringComparer.OrdinalIgnoreCase)
        {
            [".git"] = MapSkipReason.VersionControl,
            [".hg"] = MapSkipReason.VersionControl,
            [".svn"] = MapSkipReason.VersionControl,
            ["node_modules"] = MapSkipReason.Dependencies,
            [".venv"] = MapSkipReason.Dependencies,
            ["venv"] = MapSkipReason.Dependencies,
            ["__pypackages__"] = MapSkipReason.Dependencies,
            ["bin"] = MapSkipReason.BuildOutput,
            ["obj"] = MapSkipReason.BuildOutput,
            ["artifacts"] = MapSkipReason.BuildOutput,
            ["dist"] = MapSkipReason.BuildOutput,
            ["out"] = MapSkipReason.BuildOutput,
            ["target"] = MapSkipReason.BuildOutput,
            ["TestResults"] = MapSkipReason.BuildOutput,
            ["__pycache__"] = MapSkipReason.BuildOutput,
            [".vs"] = MapSkipReason.BuildOutput,
            [".next"] = MapSkipReason.BuildOutput,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static FileConvention? Recognise(MapFileFacts facts) => Files.FirstOrDefault(rule => rule.Matches(facts));

    private static bool IsWrittenToRun(MapFileFacts f)
    {
        string? text = f.Text;
        if (text is null) return false;
        if (text.StartsWith("#!", StringComparison.Ordinal)) return true;
        return f.Extension == ".py" && text.Contains("__name__", StringComparison.Ordinal) && text.Contains("__main__", StringComparison.Ordinal);
    }
}
