using System.Text.Json.Nodes;

namespace Deckle.Anytype.Mcp;

// ─── Tool descriptor ──────────────────────────────────────────────────────────
//
// One entry per MCP tool: its advertised name, its description, the JSON Schema
// for its arguments, and the handler that validates + invokes the gesture.
//
// Handler contract: returns the gesture digest on success; throws on any
// execution failure (bad input, NotFoundException, AmbiguousNameException, HTTP).
// The host maps every handler exception to a tools/call result with isError:true.

public sealed record ToolDescriptor(
    string Name,
    string Description,
    JsonObject InputSchema,
    Func<JsonObject?, CancellationToken, Task<string>> Handler);
