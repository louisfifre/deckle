using Deckle.Anytype;
using Deckle.Anytype.Mcp;

namespace Deckle.Home;

// The complete plug-in unit for Home: client identity, bearer coordinates and
// the stateless surface mounted by the resident host. Deckle.App chooses
// whether to compose this client; the generic host has no Home dependency.
public static class HomeMcp
{
    public static readonly McpClientProfile Client = new(
        "home",
        new McpSurface("home", Open),
        "mcp-token-home",
        "DECKLE_MCP_TOKEN_HOME");

    private static readonly McpSurfaceDescriptor Descriptor = new(
        "deckle-home",
        "Deckle Home",
        "Guarded shared-house space in the dedicated Home Anytype profile. "
        + "Use typed *_create and *_update for individual records; create/update keep batch support. "
        + "Start from known objects and read before editing. Before creating a task or worksite, "
        + "search relevant tasks and worksites in the stated context; use a few focused queries. "
        + "Look at active work first, then completed work if needed. Do not search unrelated projects "
        + "when a worksite is explicit. A failed read is not evidence that an object is absent. "
        + "Ask a focused question when the target or action is ambiguous; leave unknown facts empty. "
        + "Search returns structured candidates for clients that can offer choices. "
        + "Point means a connection or fixed control with its fitting; hardwired equipment remains a Device. "
        + "One physical bulb is one Device, connected_to its Point; a System is optional. "
        + "Device connection, circuit membership and controls are separate relations. "
        + "Point codes derive their room and category from the live room registry. Codes are immutable. "
        + "Supplied relation arrays replace the stored list. Use a relation's add/remove edit to change members while retaining other links. "
        + "Components still require a System. Products use needed (Prendre); complete is for work. "
        + "Initial text stores dictation in the body; updates append text or replace an existing section. "
        + "Existing select options are resolved by key or label; missing options require clarification. "
        + "Files are still added in Anytype. Content is French.");

    private static McpSurfaceBinding Open(AnytypeApiClient api)
    {
        // Alias and schema are runtime configuration. Construction stays lazy
        // so initialize/tools/list work while Home still needs provisioning; a
        // failed resolution is not cached and a later request can retry.
        HomeGestures? gestures = null;
        HomeGestures ResolveGestures() => gestures ??= new HomeGestures(
            api,
            AnytypeSpaceAliases.Load(api.SpaceId).Resolve("home"));

        return new McpSurfaceBinding(HomeToolCatalog.Build(ResolveGestures), Descriptor);
    }
}
