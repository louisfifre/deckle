namespace Deckle.Anytype.Mcp;

// The two callers the resident HTTP host serves, and the surface each one gets.
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

// The per-client surfaces mirror what the stdio era served: claude launched with
// ["--management"] over the project-management profile, codex with
// ["--profile","all"] and no management catalog. Collapsing those two exes into one
// in-process host means the surface distinction now rides the authenticated bearer
// instead of the launch args, but the shape each client sees is unchanged. The
// env-var names were frozen 2026-06-19 (JOURNAL) — the external clients read them by
// name, so they are a wire contract, not an implementation detail.
public static class McpClients
{
    public static readonly McpClientProfile Claude = new(
        "claude", ToolProfile.ProjectManagement, true, "mcp-token-claude", "DECKLE_MCP_TOKEN_CLAUDE");

    public static readonly McpClientProfile Codex = new(
        "codex", ToolProfile.All, false, "mcp-token-codex", "DECKLE_MCP_TOKEN_CODEX");

    public static readonly McpClientProfile SchemaAdmin = new(
        "schema-admin", ToolProfile.SchemaAdmin, false,
        "mcp-token-schema-admin", "DECKLE_MCP_TOKEN_SCHEMA_ADMIN");

    public static readonly IReadOnlyList<McpClientProfile> All = new[] { Claude, Codex, SchemaAdmin };
}
