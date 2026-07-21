using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp;

namespace Deckle.Home;

public static class HomeToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> Build(Func<HomeGestures> gestures)
    {
        ArgumentNullException.ThrowIfNull(gestures);

        return
        [
            new ToolDescriptor(
                "create",
                "Create one or more Home inventory objects of one type. Codes are stored in immutable object titles; element titles are exactly their codes. Room prefixes are checked against the live Pièce objects, never a compiled registry. Optional collections are Anytype memberships, not relation properties.",
                CreateSchema(),
                (args, ct) => gestures().CreateAsync(
                    RequiredString(args, "type"), CreateItems(args), ct)),

            new ToolDescriptor(
                "update",
                "Update one or more Home objects. Codes are immutable; for elements, Pièce and Catégorie are derived from the code and cannot be changed directly. Relations accept object codes, names, or ids. Collection membership uses add_to_collections/remove_from_collections and is distinct from relations. Set Existence to Déposé instead of deleting an element.",
                UpdateSchema(),
                (args, ct) => gestures().UpdateAsync(UpdateItems(args), ct)),

            new ToolDescriptor(
                "get",
                "Read one Home object in full with relation targets resolved to readable names or codes.",
                ObjectSchema(
                    required: [("object", StringSchema("Object code, name, or id."))]),
                (args, ct) => gestures().GetAsync(RequiredString(args, "object"), ct)),

            new ToolDescriptor(
                "search",
                "List Home objects with optional text, type, room, circuit, category, existence, and condition filters. All filters combine; omit every filter to list the inventory.",
                ObjectSchema(optional:
                [
                    ("text", StringSchema("Text matched against names, codes, and property values.")),
                    ("type", EnumSchema("Home type key.", HomeSchema.CreatableTypes)),
                    ("room", StringSchema("Room code, name, or id.")),
                    ("circuit", StringSchema("Circuit code, name, or id.")),
                    ("category", EnumSchema("Element category code.", HomeCategories.All)),
                    ("existence", StringSchema("Existence key or label: existant, prévu, déposé.")),
                    ("condition", StringSchema("Condition key or label: bon, vétuste, endommagé, hors service.")),
                ]),
                (args, ct) => gestures().SearchAsync(new HomeSearchFilter(
                    OptionalString(args, "text"),
                    OptionalString(args, "type"),
                    OptionalString(args, "room"),
                    OptionalString(args, "circuit"),
                    OptionalString(args, "category"),
                    OptionalString(args, "existence"),
                    OptionalString(args, "condition")), ct)),

            new ToolDescriptor(
                "delete",
                "Move a non-element Home object to Anytype's recoverable bin. First call without confirm to preview and obtain the pinned id, then repeat with that exact id and confirm:true. Elements cannot be deleted; update their Existence to Déposé.",
                ObjectSchema(
                    required: [("object", StringSchema("Object code, name, or pinned id."))],
                    optional: [("confirm", BooleanSchema("Confirm the previewed deletion; default false."))]),
                (args, ct) => gestures().DeleteAsync(
                    RequiredString(args, "object"), OptionalBoolean(args, "confirm") ?? false, ct)),
        ];
    }

    private static JsonObject CreateSchema() => ObjectSchema(
        required:
        [
            ("type", EnumSchema("Home type key shared by every item in the batch.", HomeSchema.CreatableTypes)),
            ("items", new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["maxItems"] = 100,
                ["description"] = "Objects to create after validating the whole batch.",
                ["items"] = ObjectSchema(
                    required: [("code", StringSchema("Normative immutable code."))],
                    optional:
                    [
                        ("name", StringSchema("Human title suffix for rooms, circuits, and distribution boards; forbidden for elements.")),
                        ("properties", PropertyMapSchema()),
                        ("collections", StringArraySchema("Collections to add the created object to, by name, code, or id.")),
                    ]),
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
                    required: [("object", StringSchema("Object code, name, or id."))],
                    optional:
                    [
                        ("name", StringSchema("New human title suffix for a non-element object; its code prefix is preserved.")),
                        ("properties", PropertyMapSchema()),
                        ("add_to_collections", StringArraySchema("Collections to add the object to, by name, code, or id.")),
                        ("remove_from_collections", StringArraySchema("Collections to remove the object from, by name, code, or id.")),
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

    private static JsonObject BooleanSchema(string description) =>
        new() { ["type"] = "boolean", ["description"] = description };

    private static JsonObject StringArraySchema(string description) => new()
    {
        ["type"] = "array",
        ["description"] = description,
        ["items"] = new JsonObject { ["type"] = "string" },
        ["uniqueItems"] = true,
    };

    private static JsonObject PropertyMapSchema() => new()
    {
        ["type"] = "object",
        ["description"] = "Map of live Home-schema property key or display name to value.",
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

    private static IReadOnlyList<HomeCreateItem> CreateItems(JsonObject? args)
    {
        JsonArray array = RequiredArray(args, "items");
        var result = new List<HomeCreateItem>(array.Count);
        foreach (JsonNode? node in array)
        {
            JsonObject item = RequiredObject(node, "items[]");
            RequireOnly(item, ["code", "name", "properties", "collections"], "items[]");
            result.Add(new HomeCreateItem(
                RequiredString(item, "code"),
                OptionalString(item, "name"),
                OptionalObject(item, "properties"),
                OptionalStringArray(item, "collections")));
        }
        return result;
    }

    private static IReadOnlyList<HomeUpdateItem> UpdateItems(JsonObject? args)
    {
        JsonArray array = RequiredArray(args, "items");
        var result = new List<HomeUpdateItem>(array.Count);
        foreach (JsonNode? node in array)
        {
            JsonObject item = RequiredObject(node, "items[]");
            RequireOnly(
                item,
                ["object", "name", "properties", "add_to_collections", "remove_from_collections"],
                "items[]");
            result.Add(new HomeUpdateItem(
                RequiredString(item, "object"),
                OptionalString(item, "name"),
                OptionalObject(item, "properties"),
                OptionalStringArray(item, "add_to_collections"),
                OptionalStringArray(item, "remove_from_collections")));
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

    private static IReadOnlyList<string>? OptionalStringArray(JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out JsonNode? node) || node is null) return null;
        if (node is not JsonArray array)
            throw new ArgumentException($"Argument '{name}' must be an array.", name);

        var result = new List<string>(array.Count);
        foreach (JsonNode? item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out string? text) && text is not null)
                result.Add(text);
            else
                throw new ArgumentException($"Every value in '{name}' must be a string.", name);
        }
        return result;
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

    private static bool? OptionalBoolean(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out JsonNode? node) || node is null) return null;
        if (node is JsonValue value && value.TryGetValue<bool>(out bool boolean)) return boolean;
        throw new ArgumentException($"Argument '{name}' must be a boolean.", name);
    }

    private static void RequireOnly(JsonObject value, IReadOnlyCollection<string> allowed, string owner)
    {
        foreach (string key in value.Select(pair => pair.Key))
            if (!allowed.Contains(key))
                throw new ArgumentException($"Unknown field '{key}' in '{owner}'.");
    }
}
