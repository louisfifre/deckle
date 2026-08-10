using System.Globalization;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

internal sealed class HomePropertyWriter(
    AnytypeApiClient api,
    string spaceId,
    HomeSchemaRuntime schema,
    HomeObjectIndex objects)
{
    public async Task<JsonArray> BuildAsync(
        string typeKey,
        JsonObject? values,
        IReadOnlyCollection<string>? reservedKeys,
        CancellationToken ct)
    {
        var result = new JsonArray();
        if (values is null) return result;

        foreach ((string nameOrKey, JsonNode? value) in values)
        {
            SchemaPropertyInfo property = schema.ResolveProperty(typeKey, nameOrKey);
            if (reservedKeys?.Contains(property.Key) == true)
                throw new InvalidOperationException(
                    $"La propriété « {property.Name} » est déduite du code et ne peut pas être fournie directement.");
            result.Add(await BuildEntryAsync(property, value, ct).ConfigureAwait(false));
        }
        return result;
    }

    public async Task<JsonObject> BuildEntryAsync(
        SchemaPropertyInfo property, JsonNode? value, CancellationToken ct)
    {
        if (value is null && property.Format is not "text" and not "objects" and not "multi_select")
            throw new ArgumentException($"La propriété « {property.Name} » ne peut pas recevoir null.");

        return property.Format switch
        {
            "number" => new JsonObject { ["key"] = property.Key, ["number"] = Number(value, property.Name) },
            "checkbox" => new JsonObject { ["key"] = property.Key, ["checkbox"] = Boolean(value, property.Name) },
            "date" => new JsonObject { ["key"] = property.Key, ["date"] = Text(value, property.Name) },
            "select" => new JsonObject
            {
                ["key"] = property.Key,
                ["select"] = await ResolveTagAsync(property, Text(value, property.Name), ct).ConfigureAwait(false),
            },
            "multi_select" => new JsonObject
            {
                ["key"] = property.Key,
                ["multi_select"] = await ResolveTagsAsync(property, value, ct).ConfigureAwait(false),
            },
            "objects" => new JsonObject
            {
                ["key"] = property.Key,
                ["objects"] = ResolveObjects(property, value),
            },
            "files" => throw new InvalidOperationException(
                $"La propriété « {property.Name} » porte des fichiers : dépose-les dans l'app Anytype, le MCP ne les écrit pas."),
            _ => new JsonObject { ["key"] = property.Key, ["text"] = value is null ? "" : Text(value, property.Name) },
        };
    }

    private async Task<string> ResolveTagAsync(
        SchemaPropertyInfo property, string requested, CancellationToken ct)
    {
        string normalized = requested.Trim();
        if (HomeSchema.ClosedVocabularies.TryGetValue(property.Key, out IReadOnlyList<string>? optionKeys))
        {
            string[] matches = optionKeys.Where(key =>
                    string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        HomeSchema.OptionLabel(property.Key, key), normalized,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Valeur inconnue « {requested} » pour {property.Name}. Valeurs admises : "
                    + string.Join(", ", HomeSchema.OptionLabels(property.Key)));
            normalized = matches[0];
            string expectedName = HomeSchema.OptionLabel(property.Key, normalized);
            SchemaTagInfo? live = schema.TagsFor(property.Key).Values.Distinct().FirstOrDefault(tag =>
                string.Equals(tag.Key, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag.Name, expectedName, StringComparison.OrdinalIgnoreCase));
            if (live is not null && live.Id.Length > 0) return live.Id;
        }

        IReadOnlyList<SchemaTagInfo> liveTags = await ReadTagsAsync(property, ct).ConfigureAwait(false);
        SchemaTagInfo[] candidates = liveTags.Where(tag =>
                string.Equals(tag.Key, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag.Name, requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 1 && candidates[0].Id.Length > 0) return candidates[0].Id;

        throw new InvalidOperationException(
            $"Option inconnue « {requested} » pour {property.Name}. Options présentes : "
            + string.Join(", ", liveTags.Select(tag => tag.Name).Where(name => name.Length > 0).Distinct()));
    }

    private async Task<JsonArray> ResolveTagsAsync(
        SchemaPropertyInfo property, JsonNode? value, CancellationToken ct)
    {
        var result = new JsonArray();
        if (value is null) return result;
        foreach (JsonNode? item in Enumerate(value))
            result.Add(await ResolveTagAsync(property, Text(item, property.Name), ct).ConfigureAwait(false));
        return result;
    }

    private JsonArray ResolveObjects(SchemaPropertyInfo property, JsonNode? value)
    {
        var result = new JsonArray();
        if (value is null) return result;

        // The floor targets are the app-created collection objects of the
        // runtime floor type — a compiled type list cannot name them.
        if (string.Equals(property.Key, HomeSchema.Properties.Floor, StringComparison.Ordinal))
        {
            foreach (JsonNode? item in Enumerate(value))
                result.Add(HomeObjectJson.Id(ResolveFloor(Text(item, property.Name))));
            return result;
        }

        HomeSchema.ObjectPropertyTargets.TryGetValue(property.Key, out IReadOnlyList<string>? allowedTypes);
        foreach (JsonNode? item in Enumerate(value))
        {
            string selector = Text(item, property.Name);
            result.Add(HomeObjectJson.Id(objects.Resolve(selector, allowedTypes)));
        }
        return result;
    }

    private JsonObject ResolveFloor(string selector)
    {
        if (schema.FloorTypeKey is null)
            throw new InvalidOperationException(
                "Le type Espace n'existe pas encore dans l'espace Anytype : crée-le dans "
                + "l'app (layout Collection) avant de poser une relation Espace.");

        JsonObject target = objects.ResolveCollection(selector);
        if (!string.Equals(HomeObjectJson.TypeKey(target), schema.FloorTypeKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"« {HomeObjectIndex.Display(target)} » n'est pas un Espace : "
                + "la relation Espace vise les collections du type Espace, pas une autre collection.");
        return target;
    }

    // A scalar must be wrapped in a plain array, never a JsonArray: building a
    // JsonArray around a node that already belongs to the caller's properties
    // object re-parents it and throws "The node already has a parent".
    private static IEnumerable<JsonNode?> Enumerate(JsonNode value) =>
        value is JsonArray array ? array : new JsonNode?[] { value };

    private async Task<IReadOnlyList<SchemaTagInfo>> ReadTagsAsync(
        SchemaPropertyInfo property, CancellationToken ct)
    {
        var result = new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);
        int offset = 0;
        const int limit = 100;
        while (true)
        {
            JsonObject root = await api.ListPropertyTagsForSpaceAsync(
                spaceId, property.Id, offset, limit, ct).ConfigureAwait(false);
            if (root["data"] is JsonArray data)
                foreach (JsonNode? node in data)
                {
                    if (node is not JsonObject raw) continue;
                    JsonObject value = HomeObjectJson.Unwrap(raw);
                    string key = HomeObjectJson.String(value, "key");
                    string name = HomeObjectJson.String(value, "name");
                    string id = HomeObjectJson.String(value, "id");
                    string color = HomeObjectJson.String(value, "color");
                    result[id.Length > 0 ? id : key + "\0" + name] = new SchemaTagInfo(id, key, name, color);
                }
            if (!(root["pagination"]?["has_more"]?.GetValue<bool>() ?? false)) break;
            offset += limit;
        }
        return result.Values.ToArray();
    }

    private static string Text(JsonNode? value, string propertyName)
    {
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out string? text) && text is not null)
            return text;
        throw new ArgumentException($"La propriété « {propertyName} » attend une chaîne.");
    }

    private static double Number(JsonNode? value, string propertyName)
    {
        // Parsed JSON numbers read as double; in-memory nodes may be backed
        // by the CLR integer they were built from.
        if (value is JsonValue scalar)
        {
            if (scalar.TryGetValue<double>(out double number)) return number;
            if (scalar.TryGetValue<long>(out long integer)) return integer;
            if (scalar.TryGetValue<int>(out int smallInteger)) return smallInteger;
            if (scalar.TryGetValue<string>(out string? text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return number;
        }
        throw new ArgumentException($"La propriété « {propertyName} » attend un nombre.");
    }

    private static bool Boolean(JsonNode? value, string propertyName)
    {
        if (value is JsonValue scalar && scalar.TryGetValue<bool>(out bool boolean)) return boolean;
        throw new ArgumentException($"La propriété « {propertyName} » attend un booléen.");
    }
}
