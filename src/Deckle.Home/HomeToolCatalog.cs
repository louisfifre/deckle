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
                "Create one or more Home objects of one type. Titles are human names; coded types (room, point, circuit, panel) also require an immutable code in the item's code field, stored in the Code property — a point code follows PIÈCE-CAT[SUB]NN and its room prefix is checked against the live Pièce objects, never a compiled registry; a point's room and category derive from its code. A circuit may omit its name and start titled by its code. Free-titled types take no code; an idea takes text whose first line becomes its title. A component requires 'Fait partie de' (an existing Système) — prefer component_create. An optional template names one of the type's templates by its app label, so the object is born with that structure. Optional collections are Anytype memberships, not relation properties.",
                CreateSchema(),
                (args, ct) => gestures().CreateAsync(
                    RequiredString(args, "type"), CreateItems(args), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new ToolDescriptor(
                "update",
                "Update one or more Home objects. Codes are immutable; a point's room and category are derived from its code and cannot be changed directly. Titles are renamable human names, except an idea whose title is the first line of its body. A component cannot clear 'Fait partie de' — retype it in the app instead. Relations accept object codes, names, or ids. Collection membership uses add_to_collections/remove_from_collections and is distinct from relations. Set Existence to Déposé instead of deleting a point.",
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
                "List Home objects with optional text, type, room, circuit, category, existence, condition, done, worksite, state, and system filters. All filters combine; omit every filter to list the space.",
                ObjectSchema(optional:
                [
                    ("text", StringSchema("Text matched against names, codes, and property values.")),
                    ("type", EnumSchema("Home type key.", HomeSchema.CreatableTypes)),
                    ("room", StringSchema("Room code, name, or id — matches Installé dans and Rangé dans.")),
                    ("circuit", StringSchema("Circuit code, name, or id.")),
                    ("category", EnumSchema("Point category code.", HomeCategories.All)),
                    ("existence", StringSchema("Existence key or label: existant, prévu, déposé.")),
                    ("condition", StringSchema("Condition key or label: bon, vétuste, endommagé, hors service.")),
                    ("done", BooleanSchema("Filter on the native done checkbox (errands, todos): true for checked, false for unchecked.")),
                    ("worksite", StringSchema("Chantier name or id: keep objects whose Chantier relation targets it.")),
                    ("state", StringSchema("Statut key or label: ouvert, en cours, en attente, dormant, terminé, abandonné.")),
                    ("system", StringSchema("Système name or id: keep objects whose Fait partie de targets it.")),
                ]),
                (args, ct) => gestures().SearchAsync(new HomeSearchFilter(
                    OptionalString(args, "text"),
                    OptionalString(args, "type"),
                    OptionalString(args, "room"),
                    OptionalString(args, "circuit"),
                    OptionalString(args, "category"),
                    OptionalString(args, "existence"),
                    OptionalString(args, "condition"),
                    OptionalBoolean(args, "done"),
                    OptionalString(args, "worksite"),
                    OptionalString(args, "state"),
                    OptionalString(args, "system")), ct),
                ToolExecutionContract.ReadOnly),

            new ToolDescriptor(
                "delete",
                "Move a Home object to Anytype's recoverable bin. First call without confirm to preview and obtain the pinned id, then repeat with that exact id and confirm:true. A point that describes reality never deletes — a referenced point refuses with Existence = Déposé guidance; only an entry mistake (referenced by nothing) may be retracted.",
                ObjectSchema(
                    required: [("object", StringSchema("Object code, name, or pinned id."))],
                    optional: [("confirm", BooleanSchema("Confirm the previewed deletion; default false."))]),
                (args, ct) => gestures().DeleteAsync(
                    RequiredString(args, "object"), OptionalBoolean(args, "confirm") ?? false, ct),
                ToolExecutionContract.DestructiveVerifiable),

            new ToolDescriptor(
                "component_create",
                "Create a Composant inside its Système — the system argument is mandatory because a composant is defined by its 'Fait partie de' relation, not its nature. If the system has no name, it is not a composant: create an Appareil instead (retyping is cheap).",
                ObjectSchema(
                    required:
                    [
                        ("name", StringSchema("Human component name.")),
                        ("system", StringSchema("Existing Système, by name or id.")),
                    ],
                    optional: [("properties", PropertyMapSchema())]),
                (args, ct) => gestures().CreateComponentAsync(
                    RequiredString(args, "name"),
                    RequiredString(args, "system"),
                    OptionalObject(args, "properties"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new ToolDescriptor(
                "plant_create",
                "Create a plant: a free name, optionally the room it lives in, and its botanical properties when known. Select vocabularies (famille, genre, exposition, substrat) grow from the app — options are applied, never invented here.",
                ObjectSchema(
                    required: [("name", StringSchema("Plant name as the household calls it."))],
                    optional:
                    [
                        ("room", StringSchema("Room the plant lives in, by code, name, or id.")),
                        ("properties", PropertyMapSchema()),
                    ]),
                (args, ct) => gestures().CreatePlantAsync(
                    RequiredString(args, "name"),
                    OptionalString(args, "room"),
                    OptionalObject(args, "properties"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new ToolDescriptor(
                "worksite_create",
                "Open a chantier — one finite piece of house work. Creation is deliberately loose: a name suffices; statut, concerne, date cible, and notes are added when known, to prioritize and list. Close it later with complete (statut = Terminé).",
                ObjectSchema(
                    required: [("name", StringSchema("Free chantier title."))],
                    optional:
                    [
                        ("properties", PropertyMapSchema()),
                        ("collections", StringArraySchema("Collections to add the chantier to, by name or id.")),
                    ]),
                (args, ct) => gestures().CreateWorksiteAsync(
                    RequiredString(args, "name"),
                    OptionalObject(args, "properties"),
                    OptionalStringArray(args, "collections"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new ToolDescriptor(
                "todo_create",
                "Create a house task. Orphan by default — small chores live alone; pass worksite to attach it to real works, or attach later with update. A name suffices; the native done checkbox (complete) is the completion signal, and done tasks are the chantier's history.",
                ObjectSchema(
                    required: [("name", StringSchema("Free task title."))],
                    optional:
                    [
                        ("worksite", StringSchema("Chantier to attach the task to, by name or id.")),
                        ("properties", PropertyMapSchema()),
                    ]),
                (args, ct) => gestures().CreateTodoAsync(
                    RequiredString(args, "name"),
                    OptionalString(args, "worksite"),
                    OptionalObject(args, "properties"), ct),
                ToolExecutionContract.AdditiveRequiresDeduplication),

            new ToolDescriptor(
                "complete",
                "Mark work done: checks the native done box of a todo or errand, or sets statut = Terminé on a chantier (reporting its still-open tasks). Done tasks are the record — there is no separate intervention journal.",
                ObjectSchema(
                    required: [("object", StringSchema("Tâche, course, or chantier name or id."))]),
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
                    optional:
                    [
                        ("code", StringSchema("Normative immutable code, stored in the Code property — required for room, point, circuit, and panel; forbidden for free-titled types.")),
                        ("name", StringSchema("Human title — required everywhere except an idea (title derives from text) and a circuit (falls back to its code).")),
                        ("text", StringSchema("Body text: required for an idea (first line becomes the title), optional initial body for a device, forbidden elsewhere.")),
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
                        ("name", StringSchema("New human title; the Code property is untouched. Refused for an idea.")),
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
