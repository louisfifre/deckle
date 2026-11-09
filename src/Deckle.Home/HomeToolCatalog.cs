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
                "get",
                "Read one Home object in full with relation targets resolved to readable names or codes.",
                ObjectSchema(
                    required: [("object", StringSchema("Object code, name, or id."))]),
                (args, ct) => gestures().GetAsync(RequiredString(args, "object"), ct),
                ToolExecutionContract.ReadOnly),

            new ToolDescriptor(
                "search",
                "Find Home objects within known context before writing. Filters combine; use domain and equipment_category for devices, worksite/about for tasks. Archived objects are left out unless archived: true asks for them alone. Results are paged with IDs for selection. This searches the provider's object index, not the text of bodies.",
                ObjectSchema(optional:
                [
                    ("text", StringSchema("Text matched against names, codes, and property values.")),
                    ("type", EnumSchema("Home type key.", InputTypeKeys())),
                    ("room", StringSchema("Room code, name, or id — matches Installé dans and Rangé dans.")),
                    ("circuit", StringSchema("Circuit code, name, or id.")),
                    ("category", EnumSchema("Point category code.", HomeCategories.All)),
                    ("done", BooleanSchema("Filter the native task done checkbox. For products use needed.")),
                    ("worksite", StringSchema("Chantier name or id: keep objects whose Chantier relation targets it.")),
                    ("state", StringSchema("Statut key or label: ouvert, en cours, en attente, dormant, terminé, abandonné.")),
                    ("system", StringSchema("Système name or id: keep objects whose Fait partie de targets it.")),
                    ("domain", StringSchema("Equipment domain key or live label.")),
                    ("equipment_category", StringSchema("Equipment category key or live label, e.g. bulb.")),
                    ("connected_to", StringSchema("Point code, name or id connected to a Device.")),
                    ("panel", StringSchema("Panel code, name or id attached to a circuit.")),
                    ("about", StringSchema("Home object concerned by the task or worksite.")),
                    ("needed", BooleanSchema("Prendre: true for products or equipment currently needed.")),
                    ("archived", BooleanSchema("Archivé: true returns only archived objects; by default they are left out.")),
                    ("limit", new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["default"] = 50 }),
                    ("offset", new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["default"] = 0 }),
                ]),
                async (args, ct) =>
                {
                    HomeSearchPage page = await gestures().SearchPageAsync(new HomeSearchFilter(
                    OptionalString(args, "text"),
                    OptionalString(args, "type"),
                    OptionalString(args, "room"),
                    OptionalString(args, "circuit"),
                    OptionalString(args, "category"),
                    OptionalBoolean(args, "done"),
                    OptionalString(args, "worksite"),
                    OptionalString(args, "state"),
                    OptionalString(args, "system"),
                    OptionalString(args, "domain"),
                    OptionalString(args, "equipment_category"),
                    OptionalString(args, "connected_to"),
                    OptionalString(args, "panel"),
                    OptionalString(args, "about"),
                    OptionalBoolean(args, "needed"),
                    OptionalInteger(args, "limit") ?? 50,
                    OptionalInteger(args, "offset") ?? 0,
                    OptionalBoolean(args, "archived")), ct).ConfigureAwait(false);
                    return new ToolOutput(page.Text, page.Data);
                },
                ToolExecutionContract.ReadOnly) { OutputSchema = SearchOutputSchema() },

            new ToolDescriptor(
                "delete",
                "Move a Home object to Anytype's recoverable bin. First call without confirm to preview and obtain the pinned id, show it to the person, then repeat with that exact id and confirm:true: Deckle then asks the person by a Windows notification and refuses if they say no, do not answer in time, or the notification cannot be shown. A point referenced by another object refuses — clear the references (Commande, Commandé par, Alimenté par…) with the typed *_update first; an unreferenced point deletes directly.",
                ObjectSchema(
                    required: [("object", StringSchema("Object code, name, or pinned id."))],
                    optional: [("confirm", BooleanSchema("Confirm the previewed deletion; default false."))]),
                (args, ct) => gestures().DeleteAsync(
                    RequiredString(args, "object"), OptionalBoolean(args, "confirm") ?? false, ct),
                ToolExecutionContract.DestructiveVerifiable),

            .. HomeTypedToolCatalog.Build(gestures),

            new ToolDescriptor(
                "complete",
                "Complete a task, or a worksite while reporting its remaining open tasks. Products use needed (Prendre), not task completion.",
                ObjectSchema(
                    required: [("object", StringSchema("Task or worksite name or id."))]),
                (args, ct) => gestures().CompleteAsync(RequiredString(args, "object"), ct),
                ToolExecutionContract.OverwritingIdempotent),

            new ToolDescriptor(
                "worksite_overview",
                "One-call state of a chantier: its properties, then its tasks split open / done with statut and date cible.",
                ObjectSchema(
                    required: [("worksite", StringSchema("Chantier name or id."))]),
                (args, ct) => gestures().WorksiteOverviewAsync(RequiredString(args, "worksite"), ct),
                ToolExecutionContract.ReadOnly),

            new ToolDescriptor(
                "archive",
                "Archive what had a life and should stop showing: a device, component, utensil, plant, product or idea. The object, its links and its history stay; search leaves it out unless asked. archived: false brings it back. The bin (delete) is for mistakes and removed points, not for this.",
                ObjectSchema(
                    required: [("object", StringSchema("Object code, exact name, or id."))],
                    optional: [("archived", BooleanSchema("true archives (default), false brings the object back."))]),
                (args, ct) => gestures().ArchiveAsync(
                    RequiredString(args, "object"), OptionalBoolean(args, "archived") ?? true, ct),
                ToolExecutionContract.OverwritingIdempotent),

            HomeTypedToolCatalog.PointDictateTool(gestures),
        ];
    }

    private static IEnumerable<string> InputTypeKeys() => HomeSchema.CreatableTypes;

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

    private static int? OptionalInteger(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out JsonNode? node)) return null;
        if (node is JsonValue value && value.TryGetValue<int>(out int number)) return number;
        throw new ArgumentException($"Argument '{name}' must be an integer.");
    }

    private static JsonObject SearchOutputSchema() => ObjectSchema(required:
    [
        ("matches", new JsonObject
        {
            ["type"] = "array",
            ["items"] = ObjectSchema(required:
            [
                ("id", StringSchema("Resolved object id.")),
                ("type", StringSchema("Home type key.")),
                ("name", StringSchema("Human object name.")),
                ("code", StringSchema("Inventory code, empty for uncoded objects.")),
            ]),
        }),
        ("total", new JsonObject { ["type"] = "integer" }),
        ("offset", new JsonObject { ["type"] = "integer" }),
        ("next_offset", new JsonObject { ["type"] = new JsonArray("integer", "null") }),
    ]);

    private static JsonArray RequiredArray(JsonObject? args, string name)
    {
        if (args?[name] is JsonArray array) return array;
        throw new ArgumentException($"Argument '{name}' must be an array.", name);
    }

    private static JsonObject RequiredObject(JsonNode? node, string name) =>
        node as JsonObject ?? throw new ArgumentException($"Argument '{name}' must be an object.", name);

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
