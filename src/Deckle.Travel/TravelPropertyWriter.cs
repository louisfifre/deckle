using System.Globalization;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Travel;

internal sealed class TravelPropertyWriter(
    AnytypeApiClient api,
    string spaceId,
    TravelSchemaRuntime schema,
    TravelObjectIndex objects)
{
    public async Task<JsonArray> BuildAsync(
        string typeKey,
        JsonObject? values,
        CancellationToken ct)
    {
        var result = new JsonArray();
        if (values is null) return result;

        foreach ((string nameOrKey, JsonNode? value) in values)
        {
            SchemaPropertyInfo property = schema.ResolveProperty(typeKey, nameOrKey);
            result.Add(await BuildEntryAsync(property, value, ct).ConfigureAwait(false));
        }
        return result;
    }

    public async Task<JsonObject> BuildEntryAsync(
        SchemaPropertyInfo property, JsonNode? value, CancellationToken ct)
    {
        if (value is null && property.Format is not "text" and not "objects" and not "multi_select" and not "files")
            throw new ArgumentException($"La propriété « {property.Name} » ne peut pas recevoir null.");

        return property.Format switch
        {
            "number" => new JsonObject { ["key"] = property.Key, ["number"] = Number(value, property.Name) },
            "checkbox" => new JsonObject { ["key"] = property.Key, ["checkbox"] = Boolean(value, property.Name) },
            "date" => new JsonObject { ["key"] = property.Key, ["date"] = Text(value, property.Name) },
            "url" => new JsonObject { ["key"] = property.Key, ["url"] = Text(value, property.Name) },
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
                ["objects"] = ResolveObjects(value, property.Name),
            },
            // File objects come from the Anytype files endpoint (heart v0.50.5+)
            // and live in the space like any object; the property references
            // them by id. Upload itself is not wired yet in Deckle.Anytype.
            "files" => new JsonObject
            {
                ["key"] = property.Key,
                ["files"] = ResolveObjects(value, property.Name),
            },
            _ => new JsonObject { ["key"] = property.Key, ["text"] = value is null ? "" : Text(value, property.Name) },
        };
    }

    private async Task<string> ResolveTagAsync(
        SchemaPropertyInfo property, string requested, CancellationToken ct)
    {
        string normalized = requested.Trim();
        if (TravelSchema.ClosedVocabularies.TryGetValue(property.Key, out var vocabulary))
        {
            KeyValuePair<string, string>[] matches = vocabulary.Where(pair =>
                    string.Equals(pair.Key, normalized, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Value, normalized, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Valeur inconnue « {requested} » pour {property.Name}. Valeurs admises : "
                    + string.Join(", ", vocabulary.Values));
            normalized = matches[0].Key;
            string expectedName = matches[0].Value;
            SchemaTagInfo? live = schema.TagsFor(property.Key).Values.Distinct().FirstOrDefault(tag =>
                string.Equals(tag.Key, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag.Name, expectedName, StringComparison.OrdinalIgnoreCase));
            if (live is not null && live.Id.Length > 0) return live.Id;
        }

        // Open selects (Place category) validate against the live options
        // only: a missing fit means the vocabulary lacks an option, and
        // options are added by the user in Anytype, never by the surface.
        IReadOnlyList<SchemaTagInfo> liveTags = await ReadTagsAsync(property, ct).ConfigureAwait(false);
        SchemaTagInfo[] candidates = liveTags.Where(tag =>
                string.Equals(tag.Key, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag.Name, requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length == 1 && candidates[0].Id.Length > 0) return candidates[0].Id;

        throw new InvalidOperationException(
            $"Option inconnue « {requested} » pour {property.Name}. Options présentes : "
            + string.Join(", ", liveTags.Select(tag => tag.Name).Where(name => name.Length > 0).Distinct())
            + ". Les options s’ajoutent dans Anytype, jamais par la surface.");
    }

    private async Task<JsonArray> ResolveTagsAsync(
        SchemaPropertyInfo property, JsonNode? value, CancellationToken ct)
    {
        var result = new JsonArray();
        if (value is null) return result;
        IEnumerable<JsonNode?> values = value is JsonArray array ? array : [value];
        foreach (JsonNode? item in values)
            result.Add(await ResolveTagAsync(property, Text(item, property.Name), ct).ConfigureAwait(false));
        return result;
    }

    private JsonArray ResolveObjects(JsonNode? value, string propertyName)
    {
        var result = new JsonArray();
        if (value is null) return result;
        IEnumerable<JsonNode?> values = value is JsonArray array ? array : [value];
        foreach (JsonNode? item in values)
        {
            string selector = Text(item, propertyName);
            result.Add(TravelObjectJson.Id(objects.Resolve(selector)));
        }
        return result;
    }

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
                    JsonObject value = TravelObjectJson.Unwrap(raw);
                    string key = TravelObjectJson.String(value, "key");
                    string name = TravelObjectJson.String(value, "name");
                    string id = TravelObjectJson.String(value, "id");
                    string color = TravelObjectJson.String(value, "color");
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
        if (value is JsonValue scalar && scalar.TryGetValue<double>(out double number)) return number;
        if (value is JsonValue textValue
            && textValue.TryGetValue<string>(out string? text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;
        throw new ArgumentException($"La propriété « {propertyName} » attend un nombre.");
    }

    private static bool Boolean(JsonNode? value, string propertyName)
    {
        if (value is JsonValue scalar && scalar.TryGetValue<bool>(out bool boolean)) return boolean;
        throw new ArgumentException($"La propriété « {propertyName} » attend un booléen.");
    }
}
