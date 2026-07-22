using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// Small cross-space Anytype mutations that are useful to several bounded MCPs.
// They stay outside SchemaAdminToolCatalog so future installer selection can
// mount them independently from schema provisioning.
public static class AnytypeUtilityToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> Build(
        CollectionMembershipGestures collections,
        SelectValueGestures selects) =>
    [
        new(
            "anytype_collection_add",
            "Add existing objects to one existing Anytype collection in a configured space. Object and collection arguments accept a name or id. This writes collection membership, never an object relation, and validates every target before the single additive write.",
            Schema(
                ("space", String("Configured space alias, for example dev or home.")),
                ("collection", String("Existing collection, by name or id.")),
                ("objects", StringArray("Existing objects to add, each by name or id."))),
            (args, ct) => collections.AddAsync(
                RequiredString(args, "space"),
                RequiredString(args, "collection"),
                RequiredStringArray(args, "objects"),
                ct)),

        new(
            "anytype_select_set",
            "Set one existing select or multi-select property on an existing object in a configured space. Property and option references are live Anytype keys. A select requires exactly one tag key; a multi-select receives the complete replacement list. Unknown keys are refused before the PATCH.",
            Schema(
                ("space", String("Configured space alias, for example dev or home.")),
                ("object", String("Existing object, by name or id.")),
                ("property_key", String("Existing select or multi-select property key.")),
                ("tag_keys", StringArray("Existing Anytype tag keys to write; exactly one for select."))),
            (args, ct) => selects.SetAsync(
                RequiredString(args, "space"),
                RequiredString(args, "object"),
                RequiredString(args, "property_key"),
                RequiredStringArray(args, "tag_keys"),
                ct)),
    ];

    private static JsonObject Schema(params (string Name, JsonObject Shape)[] required)
    {
        var properties = new JsonObject();
        var requiredNames = new JsonArray();
        foreach ((string name, JsonObject shape) in required)
        {
            properties[name] = shape;
            requiredNames.Add(name);
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = requiredNames,
            ["additionalProperties"] = false,
        };
    }

    private static JsonObject String(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description,
    };

    private static JsonObject StringArray(string description) => new()
    {
        ["type"] = "array",
        ["description"] = description,
        ["items"] = new JsonObject { ["type"] = "string" },
        ["minItems"] = 1,
        ["uniqueItems"] = true,
    };

    private static string RequiredString(JsonObject? args, string name)
    {
        if (args?[name] is JsonValue value
            && value.TryGetValue<string>(out string? text)
            && !string.IsNullOrWhiteSpace(text))
            return text.Trim();
        throw new ArgumentException($"Argument '{name}' must be a non-empty string.", name);
    }

    private static IReadOnlyList<string> RequiredStringArray(JsonObject? args, string name)
    {
        if (args?[name] is not JsonArray values || values.Count == 0)
            throw new ArgumentException($"Argument '{name}' must be a non-empty string array.", name);

        var result = new List<string>(values.Count);
        foreach (JsonNode? node in values)
        {
            if (node is not JsonValue value
                || !value.TryGetValue<string>(out string? text)
                || string.IsNullOrWhiteSpace(text))
                throw new ArgumentException($"Every '{name}' value must be a non-empty string.", name);
            result.Add(text.Trim());
        }
        return result;
    }
}
