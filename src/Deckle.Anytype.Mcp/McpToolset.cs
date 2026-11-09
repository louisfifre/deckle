using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// From an API client and a tool profile, builds the tool set and descriptor for
// one stateless request. The gesture graph is rebuilt on every call, so
// concurrent requests never share mutable gesture state.
public static class McpToolset
{
    public static McpSurfaceBinding Build(
        AnytypeApiClient api,
        ToolProfile profile,
        bool management,
        AnytypeSpaceAliases? aliases = null)
    {
        var resolver = new NameResolver(api, api.V2);
        var session = new SessionGestures(api, resolver, api.V2);
        var tasks = new TaskGestures(api, resolver, api.V2);
        var projects = new ProjectGestures(api, resolver, api.V2);
        var query = new QueryGestures(api, resolver, api.V2);
        var reads = new ReadGestures(resolver, api.V2);
        var documents = new DocumentGestures(api);
        var managementGestures = new ManagementGestures(api, resolver);

        IReadOnlyList<ToolDescriptor> tools = profile switch
        {
            ToolProfile.ProjectManagement => ToolCatalog.Build(
                session, tasks, projects, query, reads, documents, new ContactGestures(api, resolver, api.V2)),
            ToolProfile.SchemaAdmin => BuildSchemaAdmin(api, resolver, aliases),
            _ => throw new InvalidOperationException($"Profil MCP inconnu : {profile}."),
        };

        var descriptor = profile switch
        {
            ToolProfile.ProjectManagement => McpSurfaceDescriptor.ProjectManagement,
            ToolProfile.SchemaAdmin => McpSurfaceDescriptor.SchemaAdmin,
            _ => McpSurfaceDescriptor.ProjectManagement,
        };

        // Mount the supervised management catalog on demand, additive to the
        // object-management surface only. The schema-admin surface has no object
        // lifecycle to delete, so the flag is a no-op there.
        if (management && profile is ToolProfile.ProjectManagement)
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
