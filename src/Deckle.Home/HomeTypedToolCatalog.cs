using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp;

namespace Deckle.Home;

/// <summary>
/// The explicit, one-type-at-a-time Home surface. The generic create/update
/// tools remain the compatibility path; this catalog only owns their typed
/// counterparts and turns their flat arguments into the existing requests.
/// </summary>
public static class HomeTypedToolCatalog
{
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
        bool legacyCompatibility = IsLegacyDedicatedType(type.CanonicalKey);
        JsonObject schema = CreateSchema(type, manifest, legacyCompatibility);
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
        JsonObject value = RequireObject(args, "arguments");
        IReadOnlySet<string> allowed = CreateAllowed(type, manifest, IsLegacyDedicatedType(type.CanonicalKey));
        RequireOnly(value, allowed, type.CanonicalKey + "_create");

        string? code = OptionalText(value, "code");
        string? name = OptionalText(value, "name");
        string? text = OptionalText(value, "text");
        if (IsCoded(type.CanonicalKey) && code is null)
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
            if (properties.ContainsKey(HomeSchema.WirePropertyKey("part_of")))
                throw new ArgumentException("Use 'system' or compatibility properties.part_of, not both.", "system");
            properties[HomeSchema.WirePropertyKey("part_of")] = system;
        }

        if (type.CanonicalKey == HomeSchema.Types.Plant && value.ContainsKey("room"))
        {
            string room = OptionalText(value, "room")
                ?? throw new ArgumentException("Argument 'room' must be a string.", "room");
            if (properties.ContainsKey(HomeSchema.WirePropertyKey("installed_in")))
                throw new ArgumentException("Use 'room' or compatibility properties.installed_in, not both.", "room");
            properties[HomeSchema.WirePropertyKey("installed_in")] = room;
        }

        return await gestures().CreateAsync(
            HomeSchema.WireTypeKey(type.CanonicalKey),
            [new HomeCreateItem(code, name, properties.Count == 0 ? null : properties,
                OptionalStringArray(value, "collections"), text, OptionalText(value, "template"))],
            ct).ConfigureAwait(false);
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
            HomeSchema.WireTypeKey(type.CanonicalKey),
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

    private static JsonObject CreateSchema(ManifestType type, ManifestIndex manifest, bool compatibility)
    {
        var properties = CommonCreateProperties(type);
        foreach (ManifestProperty property in WritableProperties(type, manifest, creating: true))
            properties[property.CanonicalKey] = PropertySchema(property);
        if (type.CanonicalKey == HomeSchema.Types.Component)
            properties["system"] = StringSchema("Existing System containing this component.");
        if (type.CanonicalKey == HomeSchema.Types.Plant)
            properties["room"] = StringSchema("Room selector; compatibility alias for installed_in.");
        if (type.CanonicalKey == HomeSchema.Types.Todo)
            properties["worksite"] = StringOrStringArraySchema("Worksite selector; accepts one name/id or several for compatibility.");
        if (compatibility)
            properties["properties"] = CompatibilityPropertiesSchema(type, manifest);
        return ObjectSchema(properties, CreateRequired(type));
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
        if (args.ContainsKey("properties") && args["properties"] is not JsonObject)
            throw new ArgumentException("Argument 'properties' must be an object.", "properties");
        if (args["properties"] is JsonObject compatibility)
        {
            if (!IsLegacyDedicatedType(type.CanonicalKey))
                throw new ArgumentException("'properties' is supported only by legacy dedicated creates.", "properties");
            foreach ((string key, JsonNode? node) in compatibility)
            {
                ManifestProperty property = manifest.ResolveProperty(type, key);
                bool compatibilityPartOf = type.CanonicalKey == HomeSchema.Types.Component
                    && property.CanonicalKey == "part_of";
                if (!compatibilityPartOf && !WritableProperties(type, manifest, creating).Any(candidate =>
                        candidate.CanonicalKey == property.CanonicalKey))
                    throw new ArgumentException($"Property '{key}' is not writable for {type.CanonicalKey}.", key);
                ValidatePropertyValue(property, node, key, creating);
                string wire = HomeSchema.WirePropertyKey(property.CanonicalKey);
                if (result.ContainsKey(wire))
                    throw new ArgumentException($"Property '{key}' was supplied twice.", key);
                result[wire] = node?.DeepClone();
            }
        }

        foreach (ManifestProperty property in WritableProperties(type, manifest, creating))
        {
            if (!args.TryGetPropertyValue(property.CanonicalKey, out JsonNode? node)) continue;
            string wire = HomeSchema.WirePropertyKey(property.CanonicalKey);
            if (result.ContainsKey(wire))
                throw new ArgumentException($"Property '{property.CanonicalKey}' was supplied twice.", property.CanonicalKey);
            ValidatePropertyValue(property, node, property.CanonicalKey, creating);
            result[wire] = node?.DeepClone();
        }
        return result;
    }

    private static IEnumerable<ManifestProperty> WritableProperties(ManifestType type, ManifestIndex manifest, bool creating)
    {
        foreach (string key in type.Properties)
        {
            ManifestProperty property = manifest.ResolveProperty(type, key);
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

    private static HashSet<string> CreateAllowed(ManifestType type, ManifestIndex manifest, bool compatibility)
    {
        var set = new HashSet<string>(CommonCreateProperties(type).Select(pair => pair.Key), StringComparer.Ordinal);
        foreach (ManifestProperty property in WritableProperties(type, manifest, true)) set.Add(property.CanonicalKey);
        if (type.CanonicalKey == HomeSchema.Types.Component) set.Add("system");
        if (type.CanonicalKey == HomeSchema.Types.Plant) set.Add("room");
        if (type.CanonicalKey == HomeSchema.Types.Todo) set.Add("worksite");
        if (compatibility) set.Add("properties");
        return set;
    }

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
        if (IsCoded(type.CanonicalKey)) result.Add("code");
        if (type.CanonicalKey is not HomeSchema.Types.Idea and not HomeSchema.Types.Circuit) result.Add("name");
        if (type.CanonicalKey == HomeSchema.Types.Idea) result.Add("text");
        if (type.CanonicalKey == HomeSchema.Types.Component) result.Add("system");
        return result;
    }

    private static JsonObject CompatibilityPropertiesSchema(ManifestType type, ManifestIndex manifest)
    {
        var fields = new JsonObject();
        foreach (ManifestProperty property in WritableProperties(type, manifest, true))
            AddPropertyAliases(fields, property, PropertySchema(property));
        if (type.CanonicalKey == HomeSchema.Types.Component)
        {
            string wire = HomeSchema.WirePropertyKey("part_of");
            string french = HomeTerms.Current.PropertyName(wire);
            foreach (string alias in new[] { "part_of", wire, "Part of", french }.Distinct(StringComparer.OrdinalIgnoreCase))
                fields[alias] = StringOrStringArraySchema("Compatibility alias for system.");
        }
        return ObjectSchema(fields);
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

    private static void AddPropertyAliases(JsonObject fields, ManifestProperty property, JsonObject schema)
    {
        foreach (string alias in property.Aliases)
            if (!fields.ContainsKey(alias)) fields[alias] = schema.DeepClone();
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

    private static string CreateDescription(string type) => type is HomeSchema.Types.Todo or HomeSchema.Types.Worksite
        ? $"Create a {type} after reading existing related task/worksite context to avoid a duplicate."
        : $"Create one {type} with explicit typed fields.";

    private static string UpdateDescription(string type) => type is HomeSchema.Types.Todo or HomeSchema.Types.Worksite
        ? $"Update a {type} after reading existing related task/worksite context."
        : $"Update one {type} with explicit typed fields.";

    private static bool IsLegacyDedicatedType(string type) =>
        type is HomeSchema.Types.Component or HomeSchema.Types.Plant or HomeSchema.Types.Worksite or HomeSchema.Types.Todo;

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
        IReadOnlyList<string> Aliases,
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
                    string wire = HomeSchema.WirePropertyKey(key);
                    string name = item["name"]?.GetValue<string>() ?? key;
                    string french = HomeTerms.Current.PropertyName(wire);
                    var aliases = new[] { key, wire, name, french }
                        .Distinct(StringComparer.Ordinal).ToArray();
                    IReadOnlyList<string> closedValues = HomeSchema.ClosedVocabularies.TryGetValue(wire, out IReadOnlyList<string>? closed)
                        ? closed : [];
                    var closedLabels = options
                        .Concat(closedValues.Select(option => HomeSchema.OptionLabel(wire, option)))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    properties[key] = new ManifestProperty(
                        key, name, item["format"]?.GetValue<string>() ?? "text", options,
                        aliases, closedValues, closedLabels);
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

        public ManifestProperty ResolveProperty(ManifestType type, string key)
        {
            if (_properties.TryGetValue(key, out ManifestProperty? direct)
                && type.Properties.Contains(direct.CanonicalKey, StringComparer.Ordinal))
                return direct;
            ManifestProperty? byName = _properties.Values.FirstOrDefault(property =>
                type.Properties.Contains(property.CanonicalKey, StringComparer.Ordinal)
                && string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
            ManifestProperty? byFrenchName = _properties.Values.FirstOrDefault(property =>
                type.Properties.Contains(property.CanonicalKey, StringComparer.Ordinal)
                && string.Equals(HomeTerms.Current.PropertyName(HomeSchema.WirePropertyKey(property.CanonicalKey)), key, StringComparison.OrdinalIgnoreCase));
            if (byFrenchName is not null) return byFrenchName;
            ManifestProperty? byWire = _properties.Values.FirstOrDefault(property =>
                type.Properties.Contains(property.CanonicalKey, StringComparer.Ordinal)
                && string.Equals(HomeSchema.WirePropertyKey(property.CanonicalKey), key, StringComparison.Ordinal));
            if (byWire is not null) return byWire;
            string canonical = HomeSchema.WirePropertyKey(key);
            if (_properties.TryGetValue(canonical, out ManifestProperty? result)
                && type.Properties.Contains(result.CanonicalKey, StringComparer.Ordinal)) return result;
            throw new InvalidOperationException($"Target manifest property '{key}' is not known.");
        }
    }
}
