namespace Deckle.Atelier;

// The kinds of things the map shows, in the order the boards give their
// sections (12.8a): context files, skills, MCP servers, scripts, the standard
// files of a repository. A new kind is one member here and its rules in
// MapConventions; nothing else names the kinds one by one.
public enum MapItemKind
{
    // A file an agent reads as instructions: CLAUDE.md, AGENTS.md, GEMINI.md,
    // rules, subagent definitions, and any file one of them imports.
    ContextFile,

    // An Agent Skills folder (SKILL.md) or a Claude Code custom command.
    Skill,

    // A server declared in an MCP configuration file such as .mcp.json.
    McpServer,

    // A file a person or an agent runs: PowerShell, batch, shell, or a
    // Python or JavaScript file written to be run.
    Script,

    // README, CONTRIBUTING, LICENSE and the other files GitHub recognises.
    StandardFile,
}
