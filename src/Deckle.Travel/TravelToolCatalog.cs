using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp;

namespace Deckle.Travel;

// The bounded model-facing surface: build, plan, record, read. No delete tool
// exists on purpose — the user deletes in the Anytype app.
public static class TravelToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> Build(Func<TravelGestures> gestures)
    {
        ArgumentNullException.ThrowIfNull(gestures);

        return
        [
            new ToolDescriptor(
                "create",
                "Create one or more Travel objects of one type. Objects identify by name and links — no code grammar. Creating a stay also creates its degenerate stage (same name and dates) in the same gesture. An expense requires amount, date and a closed-vocabulary category; its stay resolves from the date when exactly one stay covers it, otherwise pass the stay explicitly. An activity with no Date sits in the pool: the Date is the state.",
                CreateSchema(),
                (args, ct) => gestures().CreateAsync(
                    RequiredString(args, "type"), CreateItems(args), ct)),

            new ToolDescriptor(
                "update",
                "Update one or more Travel objects: rename, set properties, fix an activity by giving it a Date, bind an hour with RDV, link an expense. Closed-vocabulary values must match an existing option; options are added by the user in Anytype, never by this surface.",
                UpdateSchema(),
                (args, ct) => gestures().UpdateAsync(UpdateItems(args), ct)),

            new ToolDescriptor(
                "get",
                "Read one Travel object in full with relation targets resolved to readable names.",
                ObjectSchema(
                    required: [("object", StringSchema("Object name or id."))]),
                (args, ct) => gestures().GetAsync(RequiredString(args, "object"), ct)),

            new ToolDescriptor(
                "search",
                "List Travel objects with optional text, type, stay, category, and transfer-mode filters. All filters combine; omit every filter to list the space.",
                ObjectSchema(optional:
                [
                    ("text", StringSchema("Text matched against names and property values.")),
                    ("type", EnumSchema("Travel type key.", TravelSchema.CreatableTypes)),
                    ("stay", StringSchema("Stay name or id; matches the stay itself and every object linked to it.")),
                    ("category", StringSchema("Activity, expense, or place category — key or label.")),
                    ("mode", StringSchema("Transfer mode key or label: plane, train, bus, ferry, car.")),
                ]),
                (args, ct) => gestures().SearchAsync(new TravelSearchFilter(
                    OptionalString(args, "text"),
                    OptionalString(args, "type"),
                    OptionalString(args, "stay"),
                    OptionalString(args, "category"),
                    OptionalString(args, "mode")), ct)),
        ];
    }

    private static JsonObject CreateSchema() => ObjectSchema(
        required:
        [
            ("type", EnumSchema("Travel type key shared by every item in the batch.", TravelSchema.CreatableTypes)),
            ("items", new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["maxItems"] = 100,
                ["description"] = "Objects to create after validating the whole batch.",
                ["items"] = ObjectSchema(
                    required: [("name", StringSchema("Object title; a stay is identified by destination and dates."))],
                    optional: [("properties", PropertyMapSchema())]),
            }),
        ]);

    private static JsonObject UpdateSchema() => ObjectSchema(
        required:
        [
            ("items", new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["maxItems"] = 100,
                ["description"] = "Objects to update after validating the whole batch.",
                ["items"] = ObjectSchema(
                    required: [("object", StringSchema("Object name or id."))],
                    optional:
                    [
                        ("name", StringSchema("New object title.")),
                        ("properties", PropertyMapSchema()),
                    ]),
            }),
        ]);

    private static JsonObject ObjectSchema(
        IReadOnlyList<(string Name, JsonObject Schema)>? required = null,
        IReadOnlyList<(string Name, JsonObject Schema)>? optional = null)
    {
        var properties = new JsonObject();
        var requiredNames = new JsonArray();
        if (required is not null)
            foreach ((string name, JsonObject schema) in required)
            {
                properties[name] = schema;
                requiredNames.Add(name);
            }
        if (optional is not null)
            foreach ((string name, JsonObject schema) in optional)
                properties[name] = schema;

        var result = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        if (requiredNames.Count > 0) result["required"] = requiredNames;
        return result;
    }

    private static JsonObject StringSchema(string description) =>
        new() { ["type"] = "string", ["description"] = description };

    private static JsonObject PropertyMapSchema() => new()
    {
        ["type"] = "object",
        ["description"] = "Map of live Travel-schema property key or display name to value.",
    };

    private static JsonObject EnumSchema(string description, IEnumerable<string> values)
    {
        var choices = new JsonArray();
        foreach (string value in values) choices.Add(value);
        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = choices,
        };
    }

    private static IReadOnlyList<TravelCreateItem> CreateItems(JsonObject? args)
    {
        JsonArray array = RequiredArray(args, "items");
        var result = new List<TravelCreateItem>(array.Count);
        foreach (JsonNode? node in array)
        {
            JsonObject item = RequiredObject(node, "items[]");
            RequireOnly(item, ["name", "properties"], "items[]");
            result.Add(new TravelCreateItem(
                RequiredString(item, "name"),
                OptionalObject(item, "properties")));
        }
        return result;
    }

    private static IReadOnlyList<TravelUpdateItem> UpdateItems(JsonObject? args)
    {
        JsonArray array = RequiredArray(args, "items");
        var result = new List<TravelUpdateItem>(array.Count);
        foreach (JsonNode? node in array)
        {
            JsonObject item = RequiredObject(node, "items[]");
            RequireOnly(item, ["object", "name", "properties"], "items[]");
            result.Add(new TravelUpdateItem(
                RequiredString(item, "object"),
                OptionalString(item, "name"),
                OptionalObject(item, "properties")));
        }
        return result;
    }

    private static JsonArray RequiredArray(JsonObject? args, string name)
    {
        if (args?[name] is JsonArray array) return array;
        throw new ArgumentException($"Argument '{name}' must be an array.", name);
    }

    private static JsonObject RequiredObject(JsonNode? node, string name) =>
        node as JsonObject ?? throw new ArgumentException($"Argument '{name}' must be an object.", name);

    private static JsonObject? OptionalObject(JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out JsonNode? node) || node is null) return null;
        return node as JsonObject
            ?? throw new ArgumentException($"Argument '{name}' must be an object.", name);
    }

    private static string RequiredString(JsonObject? args, string name) =>
        OptionalString(args, name)
        ?? throw new ArgumentException($"Missing required argument '{name}'.", name);

    private static string? OptionalString(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out JsonNode? node) || node is null) return null;
        if (node is JsonValue value && value.TryGetValue<string>(out string? text)) return text;
        throw new ArgumentException($"Argument '{name}' must be a string.", name);
    }

    private static void RequireOnly(JsonObject value, IReadOnlyCollection<string> allowed, string owner)
    {
        foreach (string key in value.Select(pair => pair.Key))
            if (!allowed.Contains(key))
                throw new ArgumentException($"Unknown field '{key}' in '{owner}'.");
    }
}
