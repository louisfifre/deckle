using Deckle.Anytype;
using Deckle.Anytype.Mcp;

namespace Deckle.Home;

// The complete plug-in unit for Home: client identity, bearer coordinates and
// the stateless surface mounted by the resident host. The Anytype gateway
// composes this client; the generic host has no Home dependency.
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
        + "Records are written only by the typed <type>_create and <type>_update verbs; their schemas list every field and option, so there is no vocabulary to read first. "
        + "A create takes a batch of items, validated whole before the first write. "
        + "A free-titled name that folds like an existing object of its type is refused with the candidates: update that one, or pass force: true on the item for a true twin. "
        + "Start from known objects and read before editing. Before creating a task or worksite, search the relevant tasks and worksites with a few focused queries, active work first. "
        + "A failed read is not evidence that an object is absent. Ask a focused question when the target or action is ambiguous; leave unknown facts empty. "
        + "Point means a connection or fixed control with its fitting; hardwired equipment is a Device. One physical bulb is one Device, connected_to its Point. "
        + "The person gives you a free description of a room; you extract each point and call point_create with room and category (or an explicit code) so the guichet mints the codes. Points are written at once, with no proposal round; a mistake is deleted in two steps with delete. Codes are immutable. "
        + "Supplied relation arrays replace the stored list; a relation's add/remove edit keeps the other links. A component is born in a System. "
        + "Errands are product_need, product_taken and product_list. "
        + "complete finishes a task, or a worksite with its actual_budget and actual_effort, which you ask the person for. "
        + "archive hides what had a life (device, component, utensil, plant, product, idea); the bin (delete) is for mistakes and removed points. "
        + "A plant arrives through plant_create with the name the person gives and the fields you researched (scientific name and the like). "
        + "Resolve relative dates and echo them as absolute dates. The body carries free text: initial text, then appended text or a replaced section. "
        + "Files, photos included, are added in Anytype. Content is French.");

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
