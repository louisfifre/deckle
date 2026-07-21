namespace Deckle.Anytype.Mcp;

// The callers the resident HTTP host serves, and the surface each one gets.
// A profile is the whole identity of a client: which tool profile it sees, whether
// the destructive management catalog is mounted for it, the vault name its bearer
// lives under, and the environment variable that hands that bearer to the external
// process. Reusable Anytype clients live below; custom clients live with their
// own MCP adapter and are supplied to McpClientTokens by the composition root.
public sealed record McpClientProfile(
    string Id,
    McpSurface Surface,
    string TokenSecretName,
    string TokenEnvVar);

// The legacy claude/codex surfaces still mirror what the stdio era served. New
// schema-admin rides the same authenticated-bearer routing without widening
// either legacy contract. Custom clients are declared by their own MCP adapter
// and composed by Deckle.App. Environment-variable names are wire contracts
// because external clients read them directly.
public static class McpClients
{
    private static readonly McpSurface ProjectManagementSurface = new(
        "project-management",
        api => McpToolset.Build(api, ToolProfile.ProjectManagement, management: true));

    private static readonly McpSurface AllSurface = new(
        "all",
        api => McpToolset.Build(api, ToolProfile.All, management: false));

    private static readonly McpSurface SchemaAdminSurface = new(
        "schema-admin",
        api => McpToolset.Build(api, ToolProfile.SchemaAdmin, management: false));

    public static readonly McpClientProfile Claude = new(
        "claude", ProjectManagementSurface, "mcp-token-claude", "DECKLE_MCP_TOKEN_CLAUDE");

    public static readonly McpClientProfile Codex = new(
        "codex", AllSurface, "mcp-token-codex", "DECKLE_MCP_TOKEN_CODEX");

    public static readonly McpClientProfile SchemaAdmin = new(
        "schema-admin", SchemaAdminSurface,
        "mcp-token-schema-admin", "DECKLE_MCP_TOKEN_SCHEMA_ADMIN");

    public static readonly IReadOnlyList<McpClientProfile> All =
        new[] { Claude, Codex, SchemaAdmin };
}
