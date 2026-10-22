using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// The template a create used, as the server reports it. Source says who chose
// it: "request" when the create named one, "type_default" when the type's
// default template applied because the create named none. A create whose type
// has no default template carries no receipt at all.
public sealed record AnytypeV2TemplateReceipt(string? Id, string? Name, string? Source)
{
    public const string RequestSource = "request";
    public const string TypeDefaultSource = "type_default";

    public bool IsTypeDefault => Source == TypeDefaultSource;
}

// A successful write. Body is the served JSON as it came; the accessors read
// the receipts the writes are specified to carry. ETag names the version the
// write produced, ready for the next IfMatch.
public sealed record AnytypeV2WriteReceipt(
    int Status,
    JsonObject? Body,
    string? ETag,
    bool Replayed,
    int? RateLimitRemaining)
{
    // True when the server only validated: nothing was written.
    public bool WasDryRun => Body?["dry_run"] is JsonValue value
        && value.TryGetValue(out bool dryRun) && dryRun;

    // Block ids the server minted, keyed by payload path (ops[i].blocks[j]).
    public JsonObject? CreatedBlocks => Body?["created_blocks"] as JsonObject;

    // blocks_added / removed / changed / moved and properties_changed.
    public JsonObject? DiffStats => Body?["diff_stats"] as JsonObject;

    public AnytypeV2TemplateReceipt? Template =>
        Body?["template"] is JsonObject template
            ? new AnytypeV2TemplateReceipt(
                StringOf(template, "id"),
                StringOf(template, "name"),
                StringOf(template, "source"))
            : null;

    private static string? StringOf(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
