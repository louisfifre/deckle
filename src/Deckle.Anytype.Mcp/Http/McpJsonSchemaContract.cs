using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Deckle.Anytype.Mcp;

// A compiled JSON Schema contract. Catalogs own the schema; the protocol
// adapter builds it once for the request-scoped surface and evaluates every
// value before it crosses the handler boundary.
internal sealed class McpJsonSchemaContract
{
    private readonly JsonSchema _schema;

    public McpJsonSchemaContract(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        JsonElement element = JsonSerializer.SerializeToElement(schema);
        _schema = JsonSchema.Build(
            element,
            new BuildOptions { Dialect = Dialect.Draft202012 });
    }

    public bool Accepts(JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(value);
        JsonElement element = JsonSerializer.SerializeToElement(value);
        return _schema.Evaluate(element).IsValid;
    }
}
