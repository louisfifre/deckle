using System.Text;
using System.Text.Json.Nodes;

namespace Deckle.Travel;

internal static class TravelObjectJson
{
    public static JsonObject Unwrap(JsonObject value) => value["object"] as JsonObject ?? value;

    public static string Id(JsonObject value) => String(value, "id");

    public static string Name(JsonObject value) => String(value, "name");

    public static string TypeKey(JsonObject value) =>
        value["type"]?["key"]?.GetValue<string>()
        ?? value["type_key"]?.GetValue<string>()
        ?? "";

    public static JsonObject? Property(JsonObject value, string key)
    {
        if (value["properties"] is not JsonArray properties) return null;
        foreach (JsonNode? node in properties)
            if (node is JsonObject property
                && string.Equals(String(property, "key"), key, StringComparison.Ordinal))
                return property;
        return null;
    }

    public static string DateValue(JsonObject value, string propertyKey) =>
        Property(value, propertyKey)?["date"] is JsonValue date
        && date.TryGetValue<string>(out string? text) && text is not null
            ? text
            : "";

    public static IReadOnlyList<string> ObjectReferences(JsonObject value, string? propertyKey = null)
    {
        var result = new List<string>();
        if (value["properties"] is not JsonArray properties) return result;
        foreach (JsonNode? node in properties)
        {
            if (node is not JsonObject property) continue;
            if (propertyKey is not null
                && !string.Equals(String(property, "key"), propertyKey, StringComparison.Ordinal))
                continue;
            if (property["objects"] is not JsonArray references) continue;
            foreach (JsonNode? reference in references)
                if (reference is JsonValue item && item.TryGetValue<string>(out string? id) && id is not null)
                    result.Add(id);
        }
        return result;
    }

    public static string String(JsonObject value, string key) =>
        value[key] is JsonValue node && node.TryGetValue<string>(out string? text) && text is not null
            ? text
            : "";

    public static string Render(JsonObject property, Func<string, string> objectName)
    {
        if (property["text"] is JsonValue text) return text.GetValue<string>();
        if (property["number"] is JsonValue number) return number.ToJsonString();
        if (property["checkbox"] is JsonValue checkbox) return checkbox.GetValue<bool>() ? "oui" : "non";
        if (property["date"] is JsonValue date) return date.GetValue<string>();
        if (property["url"] is JsonValue url) return url.GetValue<string>();
        if (property["select"] is JsonNode select) return SelectName(select);
        if (property["multi_select"] is JsonArray multi)
            return string.Join(", ", multi.Select(node => node is null ? "" : SelectName(node)).Where(v => v.Length > 0));
        if (property["files"] is JsonArray files) return ReferenceNames(files, objectName);
        if (property["objects"] is JsonArray objects) return ReferenceNames(objects, objectName);
        return "";
    }

    private static string ReferenceNames(JsonArray references, Func<string, string> objectName)
    {
        var names = new List<string>();
        foreach (JsonNode? node in references)
            if (node is JsonValue item && item.TryGetValue<string>(out string? id) && id is not null)
                names.Add(objectName(id));
        return string.Join(", ", names);
    }

    private static string SelectName(JsonNode value) => value switch
    {
        JsonValue scalar when scalar.TryGetValue<string>(out string? text) => text ?? "",
        JsonObject obj => String(obj, "name") is { Length: > 0 } name ? name : String(obj, "key"),
        _ => "",
    };
}

internal sealed class TravelObjectIndex
{
    private const int PageLimit = 1000;
    private readonly IReadOnlyList<JsonObject> _objects;
    private readonly IReadOnlyDictionary<string, JsonObject> _byId;

    private TravelObjectIndex(IReadOnlyList<JsonObject> objects)
    {
        _objects = objects;
        _byId = objects
            .Where(value => TravelObjectJson.Id(value).Length > 0)
            .ToDictionary(TravelObjectJson.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<JsonObject> Objects => _objects;

    public static async Task<TravelObjectIndex> LoadAsync(
        Deckle.Anytype.AnytypeApiClient api, string spaceId, CancellationToken ct)
    {
        var objects = new List<JsonObject>();
        int offset = 0;
        while (true)
        {
            JsonObject root = await api.ListObjectsAsync(spaceId, offset, PageLimit, ct)
                .ConfigureAwait(false);
            if (root["data"] is JsonArray data)
                foreach (JsonNode? node in data)
                    if (node is JsonObject value)
                        objects.Add(TravelObjectJson.Unwrap(value));

            bool hasMore = root["pagination"]?["has_more"]?.GetValue<bool>() ?? false;
            if (!hasMore) break;
            offset += PageLimit;
        }
        return new TravelObjectIndex(objects);
    }

    // A trip identifies by destination and dates, its objects by name and
    // links; there is no code grammar to fall back on, so an ambiguous name
    // answers with the candidate ids instead of guessing.
    public JsonObject Resolve(string selector, IReadOnlyCollection<string>? allowedTypes = null)
    {
        if (string.IsNullOrWhiteSpace(selector))
            throw new ArgumentException("Le sélecteur d’objet ne peut pas être vide.", nameof(selector));

        selector = selector.Trim();
        IEnumerable<JsonObject> candidates = _objects;
        if (allowedTypes is not null)
            candidates = candidates.Where(value => allowedTypes.Contains(TravelObjectJson.TypeKey(value)));
        JsonObject[] pool = candidates.ToArray();

        JsonObject[] exact = pool.Where(value =>
                string.Equals(TravelObjectJson.Id(value), selector, StringComparison.Ordinal)
                || string.Equals(TravelObjectJson.Name(value), selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exact.Length == 1) return exact[0];
        if (exact.Length > 1) throw Ambiguous(selector, exact);

        JsonObject[] partial = pool.Where(value =>
                TravelObjectJson.Name(value).Contains(selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return partial.Length switch
        {
            1 => partial[0],
            0 => throw new InvalidOperationException($"Aucun objet Travel trouvé pour « {selector} »."),
            _ => throw Ambiguous(selector, partial),
        };
    }

    public IReadOnlyList<JsonObject> OfType(string typeKey) =>
        _objects.Where(value => string.Equals(
            TravelObjectJson.TypeKey(value), typeKey, StringComparison.Ordinal)).ToArray();

    public string DisplayForId(string id) =>
        _byId.TryGetValue(id, out JsonObject? value) ? Display(value) : id;

    public static string Display(JsonObject value)
    {
        string name = TravelObjectJson.Name(value);
        return name.Length > 0 ? name : "(sans nom)";
    }

    public string Render(JsonObject value)
    {
        var builder = new StringBuilder();
        builder.Append(Display(value)).Append(" (").Append(TravelObjectJson.TypeKey(value)).Append(')');
        if (value["properties"] is JsonArray properties)
            foreach (JsonNode? node in properties)
            {
                if (node is not JsonObject property) continue;
                string rendered = TravelObjectJson.Render(property, DisplayForId);
                if (rendered.Length == 0) continue;
                string label = TravelObjectJson.String(property, "name");
                if (label.Length == 0) label = TravelObjectJson.String(property, "key");
                builder.Append('\n').Append(label).Append(" : ").Append(rendered);
            }
        string markdown = TravelObjectJson.String(value, "markdown");
        if (markdown.Length > 0) builder.Append("\n\n").Append(markdown);
        return builder.ToString();
    }

    private static InvalidOperationException Ambiguous(string selector, IReadOnlyList<JsonObject> values) =>
        new($"« {selector} » correspond à plusieurs objets Travel :\n"
            + string.Join("\n", values.Select(value =>
                $"- {TravelObjectJson.Id(value)} · {Display(value)} ({TravelObjectJson.TypeKey(value)})")));
}
