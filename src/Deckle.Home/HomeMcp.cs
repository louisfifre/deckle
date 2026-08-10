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
        "Guarded shared-house space stored in the configured Anytype Home "
        + "space: fixed inventory (rooms, points, circuits, panels), owned "
        + "equipment (systems, devices, components), house life (plants, "
        + "ideas, errands) and pilotage (worksites, todos). Titles are human "
        + "names written for the whole household — no bare acronyms; the "
        + "immutable identity code of inventory objects lives in the Code "
        + "property. Point codes follow PIÈCE-CAT[SUB]NN, their room prefix "
        + "must exist in the live room registry, and a point's room and "
        + "category derive from its code. Equipment doctrine: a Système "
        + "aggregates, an Appareil stands alone and may join a Système via "
        + "Fait partie de, a Composant only exists inside its Système — "
        + "creation without one is refused; in doubt create an Appareil, "
        + "retyping is cheap. Plants, worksites and todos have dedicated "
        + "verbs; done todos are the record. Closed vocabularies are applied, "
        + "never invented; files are deposited in the app; a point is never "
        + "deleted — set Existence to Déposé. Content is French.");

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
