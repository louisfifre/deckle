using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// An object as /v2 serves it: a flat AnyBlock document (blocks in pre-order,
// each with an integer indent) beside its typed properties, plus the ETag that
// names this version. Body is the served JSON as it came; the accessors read
// the parts the read options choose between. ETag is the one the response
// header carried, quotes included, ready for AnytypeV2WriteOptions.IfMatch.
public sealed record AnytypeV2ObjectDocument(JsonObject Body, string? ETag)
{
    public string? Id => Body["id"]?.GetValue<string>();

    public JsonObject? Properties => Body["properties"] as JsonObject;

    public JsonArray? Blocks => Body["blocks"] as JsonArray;

    // Present only on an outline read.
    public JsonArray? Outline => Body["outline"] as JsonArray;
}
