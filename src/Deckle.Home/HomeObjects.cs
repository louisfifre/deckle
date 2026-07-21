using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

internal static class HomeObjectJson
{
    public static JsonObject Unwrap(JsonObject value) => value["object"] as JsonObject ?? value;

    public static string Id(JsonObject value) => String(value, "id");

    public static string Name(JsonObject value) => String(value, "name");

    public static string TypeKey(JsonObject value) =>
        value["type"]?["key"]?.GetValue<string>()
        ?? value["type_key"]?.GetValue<string>()
        ?? "";

    public static string Layout(JsonObject value) => String(value, "layout");

    public static JsonObject? Property(JsonObject value, string key)
    {
        if (value["properties"] is not JsonArray properties) return null;
        foreach (JsonNode? node in properties)
            if (node is JsonObject property
                && string.Equals(String(property, "key"), key, StringComparison.Ordinal))
                return property;
        return null;
    }

    // Codes live in the Anytype object title, not in a duplicate property. An
    // element title is exactly its code; the other inventory types may append a
    // human label after an em dash (for example a room title).
    public static string Code(JsonObject value)
    {
        string name = Name(value).Trim();
        if (HomeSchema.ElementTypes.Contains(TypeKey(value))) return name;

        int separator = name.IndexOf(" — ", StringComparison.Ordinal);
        return separator < 0 ? name : name[..separator].Trim();
    }

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
        if (property["select"] is JsonNode select) return SelectName(select);
        if (property["multi_select"] is JsonArray multi)
            return string.Join(", ", multi.Select(node => node is null ? "" : SelectName(node)).Where(v => v.Length > 0));
        if (property["objects"] is JsonArray objects)
        {
            var names = new List<string>();
            foreach (JsonNode? node in objects)
                if (node is JsonValue item && item.TryGetValue<string>(out string? id) && id is not null)
                    names.Add(objectName(id));
            return string.Join(", ", names);
        }
        return "";
    }

    private static string SelectName(JsonNode value) => value switch
    {
        JsonValue scalar when scalar.TryGetValue<string>(out string? text) => text ?? "",
        JsonObject obj => String(obj, "name") is { Length: > 0 } name ? name : String(obj, "key"),
        _ => "",
    };
}

internal sealed class HomeObjectIndex
{
    private const int PageLimit = 1000;
    private readonly IReadOnlyList<JsonObject> _objects;
    private readonly IReadOnlyDictionary<string, JsonObject> _byId;

    private HomeObjectIndex(IReadOnlyList<JsonObject> objects)
    {
        _objects = objects;
        _byId = objects
            .Where(value => HomeObjectJson.Id(value).Length > 0)
            .ToDictionary(HomeObjectJson.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<JsonObject> Objects => _objects;

    public static async Task<HomeObjectIndex> LoadAsync(
        AnytypeApiClient api, string spaceId, CancellationToken ct)
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
                        objects.Add(HomeObjectJson.Unwrap(value));

            bool hasMore = root["pagination"]?["has_more"]?.GetValue<bool>() ?? false;
            if (!hasMore) break;
            offset += PageLimit;
        }
        return new HomeObjectIndex(objects);
    }

    public IReadOnlyDictionary<string, JsonObject> RoomRegistry()
    {
        var rooms = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonObject room in _objects.Where(value => HomeObjectJson.TypeKey(value) == HomeSchema.Types.Room))
        {
            string code;
            try { code = HomeElementCode.ValidateRoomCode(HomeObjectJson.Code(room)); }
            catch (ArgumentException)
            {
                throw new InvalidOperationException(
                    $"La pièce « {Display(room)} » ne porte pas un code valide à deux lettres.");
            }

            if (!rooms.TryAdd(code, room))
                throw new InvalidOperationException($"Le registre des pièces contient deux fois le code « {code} ».");
        }
        return rooms;
    }

    public bool ContainsCode(string code) =>
        _objects.Any(value => string.Equals(HomeObjectJson.Code(value), code, StringComparison.OrdinalIgnoreCase));

    public JsonObject Resolve(string selector, IReadOnlyCollection<string>? allowedTypes = null)
    {
        if (string.IsNullOrWhiteSpace(selector))
            throw new ArgumentException("Le sélecteur d’objet ne peut pas être vide.", nameof(selector));

        selector = selector.Trim();
        IEnumerable<JsonObject> candidates = _objects;
        if (allowedTypes is not null)
            candidates = candidates.Where(value => allowedTypes.Contains(HomeObjectJson.TypeKey(value)));
        JsonObject[] pool = candidates.ToArray();

        JsonObject[] exact = pool.Where(value =>
                string.Equals(HomeObjectJson.Id(value), selector, StringComparison.Ordinal)
                || string.Equals(HomeObjectJson.Code(value), selector, StringComparison.OrdinalIgnoreCase)
                || string.Equals(HomeObjectJson.Name(value), selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exact.Length == 1) return exact[0];
        if (exact.Length > 1) throw Ambiguous(selector, exact);

        JsonObject[] partial = pool.Where(value =>
                Display(value).Contains(selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return partial.Length switch
        {
            1 => partial[0],
            0 => throw new InvalidOperationException($"Aucun objet Home trouvé pour « {selector} »."),
            _ => throw Ambiguous(selector, partial),
        };
    }

    public JsonObject ResolveCollection(string selector)
    {
        JsonObject value = Resolve(selector);
        if (!string.Equals(HomeObjectJson.Layout(value), "collection", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"« {Display(value)} » n’est pas une collection Anytype. "
                + "L’appartenance à une collection est distincte d’une relation d’objet.");
        return value;
    }

    public string DisplayForId(string id) =>
        _byId.TryGetValue(id, out JsonObject? value) ? Display(value) : id;

    public static string Display(JsonObject value)
    {
        string code = HomeObjectJson.Code(value);
        string name = HomeObjectJson.Name(value);
        if (code.Length > 0 && name.Length > 0 && !string.Equals(code, name, StringComparison.OrdinalIgnoreCase))
            return $"{code} · {name}";
        if (code.Length > 0) return code;
        if (name.Length > 0) return name;
        return "(sans nom)";
    }

    public string Render(JsonObject value)
    {
        var builder = new StringBuilder();
        builder.Append(Display(value)).Append(" (").Append(HomeObjectJson.TypeKey(value)).Append(')');
        if (value["properties"] is JsonArray properties)
            foreach (JsonNode? node in properties)
            {
                if (node is not JsonObject property) continue;
                string rendered = HomeObjectJson.Render(property, DisplayForId);
                if (rendered.Length == 0) continue;
                string label = HomeObjectJson.String(property, "name");
                if (label.Length == 0) label = HomeObjectJson.String(property, "key");
                builder.Append('\n').Append(label).Append(" : ").Append(rendered);
            }
        string markdown = HomeObjectJson.String(value, "markdown");
        if (markdown.Length > 0) builder.Append("\n\n").Append(markdown);
        return builder.ToString();
    }

    private static InvalidOperationException Ambiguous(string selector, IReadOnlyList<JsonObject> values) =>
        new($"« {selector} » correspond à plusieurs objets Home :\n"
            + string.Join("\n", values.Select(value =>
                $"- {HomeObjectJson.Id(value)} · {Display(value)} ({HomeObjectJson.TypeKey(value)})")));
}
