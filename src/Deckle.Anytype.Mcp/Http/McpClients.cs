namespace Deckle.Anytype.Mcp;

// The callers the resident HTTP host serves, and the surface each one gets.
// A profile is the whole identity of a client: which tool profile it sees, whether
// the destructive management catalog is mounted for it, the vault name its bearer
// lives under, and the environment variable that hands that bearer to the external
// process. Adding a third client is a matter of adding a record here.
public sealed record McpClientProfile(
    string Id,
    ToolProfile Profile,
    bool Management,
    string TokenSecretName,
    string TokenEnvVar);

// The legacy claude/codex surfaces still mirror what the stdio era served. New
// narrow clients (schema-admin, Home) ride the same authenticated-bearer routing
// without widening either legacy contract. Environment-variable names are wire
// contracts because external clients read them directly.
public static class McpClients
{
    public static readonly McpClientProfile Claude = new(
        "claude", ToolProfile.ProjectManagement, true, "mcp-token-claude", "DECKLE_MCP_TOKEN_CLAUDE");

    public static readonly McpClientProfile Codex = new(
        "codex", ToolProfile.All, false, "mcp-token-codex", "DECKLE_MCP_TOKEN_CODEX");

    public static readonly McpClientProfile SchemaAdmin = new(
        "schema-admin", ToolProfile.SchemaAdmin, false,
        "mcp-token-schema-admin", "DECKLE_MCP_TOKEN_SCHEMA_ADMIN");

    public static readonly McpClientProfile Home = new(
        "home", ToolProfile.Home, false, "mcp-token-home", "DECKLE_MCP_TOKEN_HOME");

    public static readonly IReadOnlyList<McpClientProfile> All =
        new[] { Claude, Codex, SchemaAdmin, Home };
}
