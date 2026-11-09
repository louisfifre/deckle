using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp;

namespace Deckle.Home;

/// <summary>
/// The writers of Home records: one create and one update per manifest type,
/// with flat fields read from the manifest. A create takes a batch of items of
/// its type; an update takes one object. Arguments are checked here for shape
/// and unknown fields before anything reads the Home space, then handed to the
/// gestures as the existing requests.
/// </summary>
public static class HomeTypedToolCatalog
{
    private const int MaxBatchSize = 100;

    public static IReadOnlyList<ToolDescriptor> Build(Func<HomeGestures> gestures)
    {
        ArgumentNullException.ThrowIfNull(gestures);
        ManifestIndex manifest = ManifestIndex.Read(HomeSchema.TargetManifest);
        var tools = new List<ToolDescriptor>(manifest.Types.Count * 2);
        foreach (ManifestType type in manifest.Types)
        {
            tools.Add(CreateTool(type, manifest, gestures));
            tools.Add(UpdateTool(type, manifest, gestures));
        }
        return tools;
    }

    private static ToolDescriptor CreateTool(
        ManifestType type,
        ManifestIndex manifest,
        Func<HomeGestures> gestures)
    {
        JsonObject schema = CreateSchema(type, manifest);
        return new ToolDescriptor(
            type.CanonicalKey + "_create",
            CreateDescription(type.CanonicalKey),
            schema,
            (args, ct) => HandleCreateAsync(type, manifest, args, gestures, ct),
            ToolExecutionContract.AdditiveRequiresDeduplication);
    }

    private static ToolDescriptor UpdateTool(
        ManifestType type,
        ManifestIndex manifest,
        Func<HomeGestures> gestures)
    {
        return new ToolDescriptor(
            type.CanonicalKey + "_update",
            UpdateDescription(type.CanonicalKey),
            UpdateSchema(type, manifest),
            (args, ct) => HandleUpdateAsync(type, manifest, args, gestures, ct),
            ToolExecutionContract.OverwritingUncertain);
    }

    private static async Task<string> HandleCreateAsync(
        ManifestType type,
        ManifestIndex manifest,
        JsonObject? args,
        Func<HomeGestures> gestures,
        CancellationToken ct)
    {
        string tool = type.CanonicalKey + "_create";
        JsonObject value = RequireObject(args, "arguments");
        RequireOnly(value, new HashSet<string>(["items"], StringComparer.Ordinal), tool);
        JsonArray array = BoundedItems(value, "items");

        // Every item is read before the gesture runs, so a malformed second
        // item stops the batch before the space is even read.
        var items = new List<HomeCreateItem>(array.Count);
        for (int position = 0; position < array.Count; position++)
        {
            JsonObject item = array[position] as JsonObject
                ?? throw new ArgumentException($"items[{position}] must be an object.", "items");
            try
            {
                items.Add(ReadCreateItem(type, manifest, item, tool));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"items[{position}]: {ex.Message}", ex);
            }
        }

        return await gestures().CreateAsync(type.CanonicalKey, items, ct).ConfigureAwait(false);
    }

    private static HomeCreateItem ReadCreateItem(
        ManifestType type, ManifestIndex manifest, JsonObject value, string tool)
    {
        RequireOnly(value, CreateAllowed(type, manifest), tool + ".items[]");

        string? code = OptionalText(value, "code");
        string? name = OptionalText(value, "name");
        string? text = OptionalText(value, "text");
        bool isPoint = type.CanonicalKey == HomeSchema.Types.Point;
        string? category = isPoint ? OptionalText(value, "category") : null;
        string? room = isPoint ? OptionalText(value, "room") : null;
        if (isPoint && code is not null && (category is not null || room is not null))
            throw new ArgumentException("Give a point either 'code', or 'category' and 'room' to mint one, not both.", "code");
        if (isPoint && code is null && (category is null || room is null))
            throw new ArgumentException("A point needs 'code', or 'category' and 'room' to mint the lowest free code.", "code");
        if (IsCoded(type.CanonicalKey) && !isPoint && code is null)
            throw new ArgumentException("Missing required argument 'code'.", "code");
        if (!IsCoded(type.CanonicalKey) && code is not null)
            throw new ArgumentException("This type does not accept 'code'.", "code");
        if (type.CanonicalKey == HomeSchema.Types.Idea && text is null)
            throw new ArgumentException("An idea is its text: provide 'text'.", "text");
        if (type.CanonicalKey == HomeSchema.Types.Idea && name is not null)
            throw new ArgumentException("An idea has no separate name.", "name");
        if (name is null && type.CanonicalKey is not HomeSchema.Types.Idea and not HomeSchema.Types.Circuit)
            throw new ArgumentException("Missing required argument 'name'.", "name");

        JsonObject properties = BuildProperties(type, manifest, value, creating: true);
        if (type.CanonicalKey == HomeSchema.Types.Component)
        {
            string? system = OptionalText(value, "system");
            if (system is null)
                throw new ArgumentException("A component requires 'system'.", "system");
            properties[HomeSchema.Properties.PartOf] = system;
        }

        if (type.CanonicalKey == HomeSchema.Types.Plant && value.ContainsKey("room"))
        {
            string plantRoom = OptionalText(value, "room")
                ?? throw new ArgumentException("Argument 'room' must be a string.", "room");
            if (properties.ContainsKey(HomeSchema.Properties.InstalledIn))
                throw new ArgumentException("Use 'room' or 'installed_in', not both.", "room");
            properties[HomeSchema.Properties.InstalledIn] = plantRoom;
        }

        return new HomeCreateItem(
            code, name, properties.Count == 0 ? null : properties,
            OptionalStringArray(value, "collections"), text, OptionalText(value, "template"),
            OptionalBoolean(value, "force") ?? false, category, room);
    }

    private static JsonObject CategorySchema()
    {
        var values = new JsonArray();
        foreach (string choice in HomeCategories.Choices()) values.Add(choice);
        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = "Point category: its code or its label.",
            ["enum"] = values,
        };
    }

    private static async Task<string> HandleUpdateAsync(
        ManifestType type,
        ManifestIndex manifest,
        JsonObject? args,
        Func<HomeGestures> gestures,
        CancellationToken ct)
    {
        JsonObject value = RequireObject(args, "arguments");
        RequireOnly(value, UpdateAllowed(type, manifest), type.CanonicalKey + "_update");
        string objectSelector = RequiredText(value, "object");
        string? name = OptionalText(value, "name");
        if (type.CanonicalKey == HomeSchema.Types.Idea && name is not null)
            throw new ArgumentException("An idea has no separate name.", "name");

        JsonObject properties = BuildProperties(type, manifest, value, creating: false);
        JsonObject? section = null;
        if (value.ContainsKey("section"))
        {
            section = value["section"] as JsonObject
                ?? throw new ArgumentException("Argument 'section' must be an object.", "section");
            RequireOnly(section, new HashSet<string>(["heading", "text"], StringComparer.Ordinal), "section");
        }
        if (value.ContainsKey("append_text") && section is not null)
            throw new ArgumentException("Use either 'append_text' or 'section', not both.");
        HomeSectionEdit? edit = section is null
            ? null
            : new HomeSectionEdit(RequiredText(section, "heading"), RequiredText(section, "text"));
        return await gestures().UpdateTypedAsync(
            type.CanonicalKey,
            [new HomeUpdateItem(
                objectSelector,
                name,
                properties.Count == 0 ? null : properties,
                OptionalStringArray(value, "add_to_collections"),
                OptionalStringArray(value, "remove_from_collections"),
                OptionalText(value, "append_text"),
                edit)],
            ct).ConfigureAwait(false);
    }

    private static JsonObject CreateSchema(ManifestType type, ManifestIndex manifest) =>
        ObjectSchema(
            new JsonObject
            {
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = MaxBatchSize,
                    ["description"] = $"The {type.CanonicalKey} objects to create; the whole batch is validated before the first write.",
                    ["items"] = CreateItemSchema(type, manifest),
                },
            },
            ["items"]);

    private static JsonObject CreateItemSchema(ManifestType type, ManifestIndex manifest)
    {
        var properties = CommonCreateProperties(type);
        foreach (ManifestProperty property in WritableProperties(type, manifest, creating: true))
            properties[property.CanonicalKey] = PropertySchema(property);
        if (type.CanonicalKey == HomeSchema.Types.Point)
        {
            properties["code"] = StringSchema("Immutable normative code, ROOM-CATNN. Omit it and give category and room to mint the lowest free one.");
            properties["category"] = CategorySchema();
            properties["room"] = StringSchema("Room code, exact name, or id; with category, instead of code, to mint the code.");
        }
        if (type.CanonicalKey == HomeSchema.Types.Component)
            properties["system"] = StringSchema("Existing System containing this component.");
        if (type.CanonicalKey == HomeSchema.Types.Plant)
            properties["room"] = StringSchema("Room selector, a shorthand for installed_in.");
        if (type.CanonicalKey == HomeSchema.Types.Todo)
            properties["worksite"] = StringOrStringArraySchema("Worksite selector, or several: a task may serve several worksites.");
        if (HomeNameGuard.Applies(type.CanonicalKey))
            properties["force"] = BooleanSchema(
                "Pass the near-name guard for a true twin: create even though an object of this type already has a name that folds the same.");
        return ObjectSchema(properties, CreateRequired(type));
    }

    private static JsonArray BoundedItems(JsonObject value, string name)
    {
        JsonArray array = value[name] as JsonArray
            ?? throw new ArgumentException($"Argument '{name}' must be an array.", name);
        if (array.Count is 0 or > MaxBatchSize)
            throw new ArgumentException($"Argument '{name}' takes 1 to {MaxBatchSize} entries.", name);
        return array;
    }

    private static JsonObject UpdateSchema(ManifestType type, ManifestIndex manifest)
    {
        var properties = new JsonObject { ["object"] = StringSchema("Object name, code, or id.") };
        if (type.CanonicalKey != HomeSchema.Types.Idea)
            properties["name"] = StringSchema("New human title.");
        foreach (ManifestProperty property in WritableProperties(type, manifest, creating: false))
            properties[property.CanonicalKey] = PropertySchema(property, updateRelationEdits: true);
        properties["add_to_collections"] = StringArraySchema("Collections to add.");
        properties["remove_from_collections"] = StringArraySchema("Collections to remove.");
        properties["append_text"] = StringSchema("Text appended to the body.");
        properties["section"] = ObjectSchema(
            new JsonObject { ["heading"] = StringSchema("Existing heading."), ["text"] = StringSchema("Replacement text.") },
            ["heading", "text"]);
        return ObjectSchema(properties, ["object"]);
    }

    private static JsonObject BuildProperties(ManifestType type, ManifestIndex manifest, JsonObject args, bool creating)
    {
        var result = new JsonObject();
        foreach (ManifestProperty property in WritableProperties(type, manifest, creating))
        {
            if (!args.TryGetPropertyValue(property.CanonicalKey, out JsonNode? node)) continue;
            ValidatePropertyValue(property, node, property.CanonicalKey, creating);
            result[property.CanonicalKey] = node?.DeepClone();
        }
        return result;
    }

    private static IEnumerable<ManifestProperty> WritableProperties(ManifestType type, ManifestIndex manifest, bool creating)
    {
        foreach (string key in type.Properties)
        {
            ManifestProperty property = manifest.Property(type, key);
            if (property.Format == "files") continue;
            if (property.CanonicalKey == "code") continue;
            if (type.CanonicalKey == HomeSchema.Types.Point
                && property.CanonicalKey is "installed_in" or "category") continue;
            if (creating && type.CanonicalKey == HomeSchema.Types.Component && property.CanonicalKey == "part_of") continue;
            yield return property;
        }
    }

    private static void ValidatePropertyValue(ManifestProperty property, JsonNode? value, string argument, bool creating)
    {
        if (value is null)
            throw new ArgumentException($"Argument '{argument}' cannot be null.", argument);
        bool isString = value is JsonValue scalar && scalar.TryGetValue<string>(out _);
        bool isBoolean = value is JsonValue boolean && boolean.TryGetValue<bool>(out _);
        bool isNumber = value is JsonValue number
            && (number.TryGetValue<double>(out _) || number.TryGetValue<long>(out _) || number.TryGetValue<int>(out _));
        bool isStringArray = value is JsonArray array && array.All(item =>
            item is JsonValue itemValue && itemValue.TryGetValue<string>(out _));
        bool valid = property.Format switch
        {
            "number" => isNumber,
            "checkbox" => isBoolean,
            "objects" => isString || isStringArray || (!creating && IsRelationEdit(value)),
            "multi_select" => isString || isStringArray,
            "files" => false,
            _ => isString,
        };
        if (!valid)
            throw new ArgumentException($"Argument '{argument}' has the wrong shape for {property.Format}.", argument);
    }

    private static HashSet<string> CreateAllowed(ManifestType type, ManifestIndex manifest) =>
        new(CreateItemSchema(type, manifest)["properties"]!.AsObject().Select(pair => pair.Key), StringComparer.Ordinal);

    private static HashSet<string> UpdateAllowed(ManifestType type, ManifestIndex manifest)
    {
        var set = new HashSet<string>(["object", "add_to_collections", "remove_from_collections", "append_text", "section"], StringComparer.Ordinal);
        if (type.CanonicalKey != HomeSchema.Types.Idea) set.Add("name");
        foreach (ManifestProperty property in WritableProperties(type, manifest, false)) set.Add(property.CanonicalKey);
        return set;
    }

    private static JsonObject CommonCreateProperties(ManifestType type)
    {
        var result = new JsonObject
        {
            ["text"] = StringSchema(type.CanonicalKey == HomeSchema.Types.Idea ? "Complete idea body." : "Optional initial body."),
            ["template"] = StringSchema("Live template name."),
            ["collections"] = StringArraySchema("Collections to add."),
        };
        if (IsCoded(type.CanonicalKey)) result["code"] = StringSchema("Immutable normative code.");
        if (type.CanonicalKey is not HomeSchema.Types.Idea and not HomeSchema.Types.Circuit)
            result["name"] = StringSchema("Human title.");
        if (type.CanonicalKey == HomeSchema.Types.Circuit)
            result["name"] = StringSchema("Optional human title; defaults to code.");
        return result;
    }

    private static IReadOnlyList<string> CreateRequired(ManifestType type)
    {
        var result = new List<string>();
        // A point may give category and room instead of its code.
        if (IsCoded(type.CanonicalKey) && type.CanonicalKey != HomeSchema.Types.Point) result.Add("code");
        if (type.CanonicalKey is not HomeSchema.Types.Idea and not HomeSchema.Types.Circuit) result.Add("name");
        if (type.CanonicalKey == HomeSchema.Types.Idea) result.Add("text");
        if (type.CanonicalKey == HomeSchema.Types.Component) result.Add("system");
        return result;
    }

    private static JsonObject PropertySchema(ManifestProperty property, bool updateRelationEdits = false)
    {
        string options = property.ClosedValues.Count > 0
            ? " Closed vocabulary; use a listed key or label."
            : property.Options.Count > 0 ? " Existing option key or label. Initial options: " + string.Join(", ", property.Options) + "." : "";
        string description = property.Name + "." + options;
        if (updateRelationEdits && property.Format == "objects")
            return RelationUpdateSchema(description);
        if (property.ClosedValues.Count > 0)
        {
            JsonArray values = new();
            foreach (string value in property.ClosedValues.Concat(property.ClosedLabels).Distinct(StringComparer.Ordinal))
                values.Add(value);
            return new JsonObject { ["type"] = "string", ["description"] = description, ["enum"] = values };
        }
        return property.Format switch
        {
            "number" => new JsonObject { ["type"] = "number", ["description"] = description },
            "checkbox" => new JsonObject { ["type"] = "boolean", ["description"] = description },
            "multi_select" or "objects" => StringOrStringArraySchema(description),
            _ => StringSchema(description),
        };
    }

    private static JsonObject RelationUpdateSchema(string description) => new()
    {
        ["description"] = description + " Replacement accepts a selector or array; alternatively use add/remove arrays.",
        ["oneOf"] = new JsonArray(
            StringSchema(description + " Replacement selector."),
            StringArraySchema(description + " Replacement selectors."),
            ObjectSchema(
                new JsonObject
                {
                    ["add"] = StringArraySchema("Selectors to append."),
                    ["remove"] = StringArraySchema("Selectors to remove."),
                }, minProperties: 1)
        )
    };

    private static bool IsRelationEdit(JsonNode value) => value is JsonObject edit
        && edit.Count > 0
        && edit.All(pair => pair.Key is "add" or "remove" && pair.Value is JsonArray array
            && array.All(item => item is JsonValue scalar && scalar.TryGetValue<string>(out _)));

    private static string CreateDescription(string type)
    {
        string context = type is HomeSchema.Types.Todo or HomeSchema.Types.Worksite
            ? " Read the existing related tasks and worksites first to avoid a duplicate."
            : "";
        string identity = HomeNameGuard.Applies(type)
            ? " A name that folds like an existing object of this type (case, accents, punctuation, plural) is refused with the candidates: update the one that exists, or pass force: true on the item for a true twin."
            : " The code is the identity; a code already taken is refused with the next free one.";
        string pointGuide = type == HomeSchema.Types.Point
            ? " The person describes a room freely; extract each point from that description and give it room and category so the guichet mints its code"
              + " (lowest free number per room and category, gaps reused, counting the batch), or give an explicit code instead. Never give both."
              + " Points are written at once, with no proposal round; the answer lists each created point with its code, name and id."
              + " A mistake is deleted in two steps with delete. Codes are immutable."
            : "";
        return $"Create one or more {type} objects with explicit typed fields (items, 1 to {MaxBatchSize}). "
            + "The whole batch is validated before the first write; a transport failure part way is reported item by item: create only what is missing."
            + identity + context + pointGuide;
    }

    private static string UpdateDescription(string type) => type is HomeSchema.Types.Todo or HomeSchema.Types.Worksite
        ? $"Update a {type} after reading existing related task/worksite context."
        : $"Update one {type} with explicit typed fields.";

    private static bool IsCoded(string type) => HomeSchema.CodedTypes.Contains(type, StringComparer.Ordinal);

    private static JsonObject ObjectSchema(JsonObject properties, IReadOnlyList<string>? required = null, int? minProperties = null)
    {
        var result = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        if (minProperties is int minimum) result["minProperties"] = minimum;
        if (required is { Count: > 0 })
            result["required"] = new JsonArray(required.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        return result;
    }

    private static JsonObject StringSchema(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject BooleanSchema(string description) => new() { ["type"] = "boolean", ["description"] = description };

    private static bool? OptionalBoolean(JsonObject value, string name)
    {
        if (!value.TryGetPropertyValue(name, out JsonNode? node) || node is null) return null;
        if (node is JsonValue scalar && scalar.TryGetValue<bool>(out bool boolean)) return boolean;
        throw new ArgumentException($"Argument '{name}' must be a boolean.", name);
    }

    private static JsonObject StringArraySchema(string description) => new()
    {
        ["type"] = "array", ["description"] = description, ["items"] = new JsonObject { ["type"] = "string" }, ["uniqueItems"] = true,
    };

    private static JsonObject StringOrStringArraySchema(string description) => new()
    {
        ["description"] = description,
        ["oneOf"] = new JsonArray(StringSchema(description), StringArraySchema(description)),
    };

    private static JsonObject RequireObject(JsonObject? value, string owner) =>
        value ?? throw new ArgumentException($"'{owner}' must be an object.", owner);

    private static string RequiredText(JsonObject value, string name) =>
        OptionalText(value, name) ?? throw new ArgumentException($"Missing required argument '{name}'.", name);

    private static string? OptionalText(JsonObject value, string name)
    {
        if (!value.TryGetPropertyValue(name, out JsonNode? node) || node is null) return null;
        if (node is JsonValue scalar && scalar.TryGetValue<string>(out string? text)) return text;
        throw new ArgumentException($"Argument '{name}' must be a string.", name);
    }

    private static IReadOnlyList<string>? OptionalStringArray(JsonObject value, string name)
    {
        if (!value.TryGetPropertyValue(name, out JsonNode? node) || node is null) return null;
        if (node is JsonValue scalar && scalar.TryGetValue<string>(out string? text)) return [text];
        if (node is not JsonArray array) throw new ArgumentException($"Argument '{name}' must be a string or array.", name);
        var result = new List<string>(array.Count);
        foreach (JsonNode? item in array)
            if (item is JsonValue itemValue && itemValue.TryGetValue<string>(out string? itemText) && itemText is not null)
                result.Add(itemText);
            else throw new ArgumentException($"Every value in '{name}' must be a string.", name);
        return result;
    }

    private static void RequireOnly(JsonObject value, IReadOnlySet<string> allowed, string owner)
    {
        foreach (string key in value.Select(pair => pair.Key))
            if (!allowed.Contains(key)) throw new ArgumentException($"Unknown field '{key}' in '{owner}'.", owner);
    }

    private sealed record ManifestType(string CanonicalKey, IReadOnlyList<string> Properties);
    private sealed record ManifestProperty(
        string CanonicalKey,
        string Name,
        string Format,
        IReadOnlyList<string> Options,
        IReadOnlyList<string> ClosedValues,
        IReadOnlyList<string> ClosedLabels);

    private sealed class ManifestIndex
    {
        private readonly IReadOnlyDictionary<string, ManifestProperty> _properties;
        public IReadOnlyList<ManifestType> Types { get; }

        private ManifestIndex(IReadOnlyList<ManifestType> types, IReadOnlyDictionary<string, ManifestProperty> properties)
        {
            Types = types;
            _properties = properties;
        }

        public static ManifestIndex Read(JsonObject manifest)
        {
            var properties = new Dictionary<string, ManifestProperty>(StringComparer.Ordinal);
            if (manifest["properties"] is JsonArray propertyArray)
                foreach (JsonObject item in propertyArray.OfType<JsonObject>())
                {
                    string key = item["key"]?.GetValue<string>() ?? throw new InvalidOperationException("Target manifest property has no key.");
                    var options = item["tags"] is JsonArray tags
                        ? tags.OfType<JsonObject>().Select(tag => tag["name"]?.GetValue<string>() ?? tag["key"]?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToArray()
                        : [];
                    string name = item["name"]?.GetValue<string>() ?? key;
                    IReadOnlyList<string> closedValues = HomeSchema.ClosedVocabularies.TryGetValue(key, out IReadOnlyList<string>? closed)
                        ? closed : [];
                    var closedLabels = options
                        .Concat(closedValues.Select(option => HomeSchema.OptionLabel(key, option)))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    properties[key] = new ManifestProperty(
                        key, name, item["format"]?.GetValue<string>() ?? "text", options,
                        closedValues, closedLabels);
                }

            var types = new List<ManifestType>();
            if (manifest["types"] is JsonArray typeArray)
                foreach (JsonObject item in typeArray.OfType<JsonObject>())
                {
                    string key = item["key"]?.GetValue<string>() ?? throw new InvalidOperationException("Target manifest type has no key.");
                    var keys = item["properties"] is JsonArray list
                        ? list.Select(value => value?.GetValue<string>() ?? throw new InvalidOperationException("Target manifest type property is not text.")).ToArray()
                        : [];
                    types.Add(new ManifestType(key, keys));
                }
            return new ManifestIndex(types, properties);
        }

        // The tools speak the manifest keys only; a type's own list names them.
        public ManifestProperty Property(ManifestType type, string key) =>
            _properties.TryGetValue(key, out ManifestProperty? property)
                && type.Properties.Contains(property.CanonicalKey, StringComparer.Ordinal)
                ? property
                : throw new InvalidOperationException($"Target manifest property '{key}' is not known.");
    }
}
