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
                "Create a batch of one Home type. Prefer the typed *_create commands for individual records. Coded objects require a code; Point room/category derive from it. Search relevant existing tasks and worksites before creating one. The batch validates before sequential writes; a transport failure may leave a partial batch: search before retrying.",
                CreateSchema(),
                (args, ct) => gestures().CreateAsync(
                    RequiredString(args, "type"), CreateItems(args), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new ToolDescriptor(
                "update",
                "Update a Home batch. Prefer typed *_update for individual records. Read existing content first; omitted fields stay unchanged, supplied relation arrays replace their contents. Codes and Point room/category are immutable. append_text adds to the current body; section replaces one existing section. Relation fields may instead use {add:[selectors],remove:[selectors]} to retain other targets. Collections are separate from relations.",
                UpdateSchema(),
                (args, ct) => gestures().UpdateAsync(UpdateItems(args), ct),
                ToolExecutionContract.OverwritingUncertain),

            new ToolDescriptor(
                "get",
                "Read one Home object in full with relation targets resolved to readable names or codes.",
                ObjectSchema(
                    required: [("object", StringSchema("Object code, name, or id."))]),
                (args, ct) => gestures().GetAsync(RequiredString(args, "object"), ct),
                ToolExecutionContract.ReadOnly),

            new ToolDescriptor(
                "search",
                "Find Home objects within known context before writing. Filters combine; use domain and equipment_category for devices, worksite/about for tasks. Results are paged with IDs for selection. This searches the provider's object index, not a full-text body or archive service.",
                ObjectSchema(optional:
                [
                    ("text", StringSchema("Text matched against names, codes, and property values.")),
                    ("type", EnumSchema("Home type key.", InputTypeKeys())),
                    ("room", StringSchema("Room code, name, or id — matches Installé dans and Rangé dans.")),
                    ("circuit", StringSchema("Circuit code, name, or id.")),
                    ("category", EnumSchema("Point category code.", HomeCategories.All)),
                    ("condition", StringSchema("Condition key or label: bon, vétuste, endommagé, hors service.")),
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
                    OptionalString(args, "condition"),
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
                    OptionalInteger(args, "offset") ?? 0), ct).ConfigureAwait(false);
                    return new ToolOutput(page.Text, page.Data);
                },
                ToolExecutionContract.ReadOnly) { OutputSchema = SearchOutputSchema() },

            new ToolDescriptor(
                "delete",
                "Move a Home object to Anytype's recoverable bin. First call without confirm to preview and obtain the pinned id, then repeat with that exact id and confirm:true. A point referenced by another object refuses — clear the references (Commande, Commandé par, Alimenté par…) with update first; an unreferenced point deletes directly.",
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
        ];
    }

    private static IEnumerable<string> InputTypeKeys() => HomeSchema.TargetManifest["types"]!
        .AsArray().OfType<JsonObject>().Select(type => type["key"]!.GetValue<string>())
        .Concat(HomeSchema.CreatableTypes).Append(HomeSchema.Types.Floor).Distinct(StringComparer.Ordinal);

    private static JsonObject CreateSchema() => ObjectSchema(
        required:
        [
            ("type", EnumSchema("Home type key shared by every item in the batch.", InputTypeKeys())),
            ("items", new JsonObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["maxItems"] = 100,
                ["description"] = "Objects to create after validating the whole batch.",
                ["items"] = ObjectSchema(
                    optional:
                    [
                        ("code", StringSchema("Normative immutable code, stored in the Code property — required for room, point, circuit, and panel; forbidden for free-titled types.")),
                        ("name", StringSchema("Human title — required everywhere except a titleless idea and a circuit (falls back to its code).")),
                        ("text", StringSchema("Initial Markdown body; required for an idea, optional for other Home types.")),
                        ("properties", PropertyMapSchema()),
                        ("collections", StringArraySchema("Collections to add the created object to, by name, code, or id.")),
                        ("template", StringSchema("Name of one of the type's templates as shown in the app; the object is born with that template's structure. Resolved against the live type at call time and composes with text and properties.")),
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
                        ("name", StringSchema("New human title. Codes stay unchanged; ideas have no separate title.")),
                        ("properties", PropertyMapSchema()),
                        ("add_to_collections", StringArraySchema("Collections to add the object to, by name, code, or id.")),
                        ("remove_from_collections", StringArraySchema("Collections to remove the object from, by name, code, or id.")),
                        ("append_text", StringSchema("Text to append to the current body. Do not resend after an uncertain write; read first.")),
                        ("section", ObjectSchema(required: [("heading", StringSchema("Existing heading, matched uniquely.")), ("text", StringSchema("New section content; empty clears the section."))])) ,
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
            RequireOnly(
                item,
                ["code", "name", "text", "properties", "collections", "template"],
                "items[]");
            result.Add(new HomeCreateItem(
                OptionalString(item, "code"),
                OptionalString(item, "name"),
                OptionalObject(item, "properties"),
                OptionalStringArray(item, "collections"),
                OptionalString(item, "text"),
                OptionalString(item, "template")));
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
                ["object", "name", "properties", "add_to_collections", "remove_from_collections", "append_text", "section"],
                "items[]");
            result.Add(new HomeUpdateItem(
                RequiredString(item, "object"),
                OptionalString(item, "name"),
                OptionalObject(item, "properties"),
                OptionalStringArray(item, "add_to_collections"),
                OptionalStringArray(item, "remove_from_collections"),
                OptionalString(item, "append_text"),
                ReadSection(item)));
        }
        return result;
    }

    private static HomeSectionEdit? ReadSection(JsonObject item)
    {
        JsonObject? section = OptionalObject(item, "section");
        if (section is null) return null;
        RequireOnly(section, ["heading", "text"], "section");
        return new HomeSectionEdit(RequiredString(section, "heading"), RequiredString(section, "text"));
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

    private static JsonObject? OptionalObject(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out JsonNode? node) || node is null) return null;
        return node as JsonObject
            ?? throw new ArgumentException($"Argument '{name}' must be an object.", name);
    }

    private static IReadOnlyList<string>? OptionalStringArray(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out JsonNode? node) || node is null) return null;
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
