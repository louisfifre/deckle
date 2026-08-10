using System.Text.Json.Nodes;

namespace Deckle.Anytype.Mcp;

// ─── Tool descriptor ──────────────────────────────────────────────────────────
//
// One entry per MCP tool: its advertised name, its description, the JSON Schema
// for its arguments, and the handler that validates + invokes the gesture.
//
// Handler contract: returns a text fallback and, when declared, schema-backed
// structured content; throws on any execution failure (bad input,
// NotFoundException, AmbiguousNameException, HTTP). The host maps every handler
// exception to a tools/call result with isError:true.

public sealed record ToolDescriptor(
    string Name,
    string Description,
    JsonObject InputSchema,
    Func<JsonObject?, CancellationToken, Task<ToolOutput>> Handler,
    ToolExecutionContract Execution)
{
    public ToolDescriptor(
        string name,
        string description,
        JsonObject inputSchema,
        Func<JsonObject?, CancellationToken, Task<string>> handler,
        ToolExecutionContract execution)
        : this(
            name,
            description,
            inputSchema,
            async (arguments, ct) => new ToolOutput(
                await handler(arguments, ct).ConfigureAwait(false)),
            execution)
    {
    }

    public JsonObject? OutputSchema { get; init; }
}

// The model-facing result stays independent from the MCP SDK. Text is the
// universal fallback; structured content is an optional, schema-backed value
// that protocol-aware clients can validate and render without parsing prose.
public sealed record ToolOutput(string Text, JsonNode? StructuredContent = null);
