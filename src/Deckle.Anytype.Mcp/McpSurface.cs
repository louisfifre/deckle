using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// A surface is the unit the resident host can mount for one authenticated
// client. The host owns transport and identity; the surface owns only the
// session-scoped tool graph and its model-facing descriptor. Domain adapters
// can declare surfaces from their own assemblies without being referenced by
// this host module.
public sealed record McpSurface(
    string Id,
    Func<AnytypeApiClient, McpSurfaceSession> OpenSession);

public sealed record McpSurfaceSession(
    IReadOnlyList<ToolDescriptor> Tools,
    McpServer.Descriptor Descriptor);
