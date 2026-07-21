using Deckle.Anytype;
using Deckle.Home;

namespace Deckle.Anytype.Mcp;

// The composition seam Program.cs used to own: from an API client and a
// per-client profile, it builds the tool set and the descriptor for one MCP
// session. The gesture graph is rebuilt on every call because it is
// session-scoped — SessionGestures holds the current-report default that log
// targets — so two concurrent sessions must not share one instance.
public static class McpToolset
{
    public static (IReadOnlyList<ToolDescriptor> Tools, McpServer.Descriptor Descriptor) Build(
        AnytypeApiClient api,
        ToolProfile profile,
        bool management,
        AnytypeSpaceAliases? aliases = null)
    {
        var resolver = new NameResolver(api);
        var session = new SessionGestures(api, resolver);
        var tasks = new TaskGestures(api, resolver);
        var projects = new ProjectGestures(api, resolver);
        var query = new QueryGestures(api, resolver);
        var documents = new DocumentGestures(api);
        var dialogues = new DialogueGestures(api, resolver);
        var managementGestures = new ManagementGestures(api, resolver);

        // Home's alias and schema are runtime configuration. Keep construction
        // lazy so initialize/tools/list remain available when Home still needs
        // provisioning; a failed resolution is not cached, so the same session
        // can retry after the local alias is repaired.
        HomeGestures? home = null;
        HomeGestures ResolveHome() => home ??= new HomeGestures(
            api,
            (aliases ?? AnytypeSpaceAliases.Load(api.SpaceId)).Resolve("home"));

        IReadOnlyList<ToolDescriptor> tools = profile switch
        {
            ToolProfile.ProjectManagement => ToolCatalog.Build(session, tasks, projects, query, documents),
            ToolProfile.Dialogues => DialogueToolCatalog.Build(dialogues),
            ToolProfile.SchemaAdmin => SchemaAdminToolCatalog.Build(
                new SchemaAdminGestures(api, aliases ?? AnytypeSpaceAliases.Load(api.SpaceId))),
            ToolProfile.All => ToolCatalog.Build(session, tasks, projects, query, documents)
                .Concat(DialogueToolCatalog.Build(dialogues))
                .ToArray(),
            ToolProfile.Home => HomeToolCatalog.Build(ResolveHome),
            _ => throw new InvalidOperationException($"Profil MCP inconnu : {profile}."),
        };

        var descriptor = profile switch
        {
            ToolProfile.ProjectManagement => McpServer.ProjectManagementDescriptor,
            ToolProfile.Dialogues => McpServer.DialoguesDescriptor,
            ToolProfile.SchemaAdmin => McpServer.SchemaAdminDescriptor,
            ToolProfile.All => McpServer.AllDescriptor,
            ToolProfile.Home => McpServer.HomeDescriptor,
            _ => McpServer.ProjectManagementDescriptor,
        };

        // Mount the supervised management catalog on demand, additive to the
        // object-management surfaces only. Dialogue and schema-admin sessions
        // have no object lifecycle to delete, so the flag is a no-op there.
        if (management && profile is ToolProfile.ProjectManagement or ToolProfile.All)
        {
            tools = tools.Concat(ManagementToolCatalog.Build(managementGestures)).ToArray();
            descriptor = descriptor with
            {
                Instructions = descriptor.Instructions + "\n\n" + ManagementToolCatalog.Instructions,
            };
        }

        return (tools, descriptor);
    }
}
