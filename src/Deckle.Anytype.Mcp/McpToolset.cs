using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// The composition seam Program.cs used to own: from an API client and a
// per-client profile, it builds the tool set and descriptor for one stateless
// request. The gesture graph is rebuilt on every call, so concurrent requests
// never share mutable gesture state.
public static class McpToolset
{
    public static McpSurfaceBinding Build(
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

        IReadOnlyList<ToolDescriptor> tools = profile switch
        {
            ToolProfile.ProjectManagement => ToolCatalog.Build(session, tasks, projects, query, documents),
            ToolProfile.Dialogues => DialogueToolCatalog.Build(dialogues),
            ToolProfile.SchemaAdmin => BuildSchemaAdmin(api, resolver, aliases),
            ToolProfile.All => ToolCatalog.Build(session, tasks, projects, query, documents)
                .Concat(DialogueToolCatalog.Build(dialogues))
                .ToArray(),
            _ => throw new InvalidOperationException($"Profil MCP inconnu : {profile}."),
        };

        var descriptor = profile switch
        {
            ToolProfile.ProjectManagement => McpSurfaceDescriptor.ProjectManagement,
            ToolProfile.Dialogues => McpSurfaceDescriptor.Dialogues,
            ToolProfile.SchemaAdmin => McpSurfaceDescriptor.SchemaAdmin,
            ToolProfile.All => McpSurfaceDescriptor.All,
            _ => McpSurfaceDescriptor.ProjectManagement,
        };

        // Mount the supervised management catalog on demand, additive to the
        // object-management surfaces only. Dialogue and schema-admin surfaces
        // have no object lifecycle to delete, so the flag is a no-op there.
        if (management && profile is ToolProfile.ProjectManagement or ToolProfile.All)
        {
            tools = tools.Concat(ManagementToolCatalog.Build(managementGestures)).ToArray();
            descriptor = descriptor with
            {
                Instructions = descriptor.Instructions + "\n\n" + ManagementToolCatalog.Instructions,
            };
        }

        return new McpSurfaceBinding(tools, descriptor);
    }

    private static IReadOnlyList<ToolDescriptor> BuildSchemaAdmin(
        AnytypeApiClient api,
        NameResolver resolver,
        AnytypeSpaceAliases? aliases)
    {
        AnytypeSpaceAliases spaces = aliases ?? AnytypeSpaceAliases.Load(api.SpaceId);
        return SchemaAdminToolCatalog.Build(new SchemaAdminGestures(api, spaces))
            .Concat(AnytypeUtilityToolCatalog.Build(
                new CollectionMembershipGestures(api, spaces, resolver),
                new SelectValueGestures(api, spaces, resolver)))
            .ToArray();
    }
}
