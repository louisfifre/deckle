namespace Deckle.Anytype.Mcp;

// Which surface a session gets. It used to be parsed from a stdio launch arg;
// now the resident host picks it per client (Claude Code, Codex) when it builds
// that session's toolset, so it is public and the arg/env parsers are gone.
public enum ToolProfile
{
    ProjectManagement,
    Dialogues,
    SchemaAdmin,
    All,
    Home,
}
