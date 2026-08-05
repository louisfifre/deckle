using Deckle.Anytype;
using Deckle.Anytype.Mcp;

namespace Deckle.Home;

// The complete plug-in unit for Home: client identity, bearer coordinates and
// the session-scoped surface mounted by the resident host. Deckle.App chooses
// whether to compose this client; the generic host has no Home dependency.
public static class HomeMcp
{
    public static readonly McpClientProfile Client = new(
        "home",
        new McpSurface("home", OpenSession),
        "mcp-token-home",
        "DECKLE_MCP_TOKEN_HOME");

    private static readonly McpServer.Descriptor Descriptor = new(
        "deckle-home",
        "Deckle Home",
        "Guarded home inventory and house life stored in the configured "
        + "Anytype Home space. Element codes follow PIÈCE-CAT[SUB]NN and never "
        + "change; their room prefix must exist in the live room registry. "
        + "Element titles are the codes, human labels live in Libellé, and "
        + "ordinary bodies stay empty. Life and work types (idee, course, "
        + "outil, chantier, tache) are free-titled; chantiers and tâches use "
        + "the dedicated verbs, done tasks are the record. Properties and "
        + "relations carry structured facts. Unknown rooms, duplicate codes, "
        + "unknown closed-vocabulary values, and element deletion are refused "
        + "with corrective guidance. Content is French.");

    private static McpSurfaceSession OpenSession(AnytypeApiClient api)
    {
        // Alias and schema are runtime configuration. Construction stays lazy
        // so initialize/tools/list work while Home still needs provisioning; a
        // failed resolution is not cached and the session can retry later.
        HomeGestures? gestures = null;
        HomeGestures ResolveGestures() => gestures ??= new HomeGestures(
            api,
            AnytypeSpaceAliases.Load(api.SpaceId).Resolve("home"));

        return new McpSurfaceSession(HomeToolCatalog.Build(ResolveGestures), Descriptor);
    }
}
