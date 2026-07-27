using Deckle.Anytype;
using Deckle.Anytype.Mcp;

namespace Deckle.Travel;

// The complete plug-in unit for Travel: client identity, bearer coordinates
// and the session-scoped surface mounted by the resident host. Deckle.App
// chooses whether to compose this client; the generic host has no Travel
// dependency.
public static class TravelMcp
{
    public static readonly McpClientProfile Client = new(
        "travel",
        new McpSurface("travel", OpenSession),
        "mcp-token-travel",
        "DECKLE_MCP_TOKEN_TRAVEL");

    private static readonly McpServer.Descriptor Descriptor = new(
        "deckle-travel",
        "Deckle Travel",
        "Guarded trip preparation stored in the configured Anytype Travel "
        + "space. Seven types — stay, stage, place, activity, transfer, "
        + "lodging, expense — identified by name and links, no code grammar. "
        + "An activity has no status: its Date is the state (unset = pool, "
        + "set = fixed, past = done) and RDV carries the hour when it binds. "
        + "An expense is a receipt — amount, date, closed-vocabulary "
        + "category, stay — written only once certain. Unknown "
        + "closed-vocabulary values and deletion are refused with corrective "
        + "guidance. Content is French.");

    private static McpSurfaceSession OpenSession(AnytypeApiClient api)
    {
        // Alias and schema are runtime configuration. Construction stays lazy
        // so initialize/tools/list work while Travel still needs provisioning;
        // a failed resolution is not cached and the session can retry later.
        TravelGestures? gestures = null;
        TravelGestures ResolveGestures() => gestures ??= new TravelGestures(
            api,
            AnytypeSpaceAliases.Load(api.SpaceId).Resolve("travel"));

        return new McpSurfaceSession(TravelToolCatalog.Build(ResolveGestures), Descriptor);
    }
}
