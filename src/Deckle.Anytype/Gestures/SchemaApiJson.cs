using System.Text.Json.Nodes;

namespace Deckle.Anytype;

internal static class SchemaApiJson
{
    public static IEnumerable<JsonObject> Data(JsonObject root)
    {
        if (root["data"] is not JsonArray data) yield break;
        foreach (JsonNode? node in data)
            if (node is JsonObject obj)
                yield return obj["object"] as JsonObject ?? obj;
    }

    public static string Id(JsonObject obj) => Str(obj, "id");

    public static JsonObject Payload(JsonObject obj) => obj["object"] as JsonObject ?? obj;

    public static string Str(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out string? s) && s is not null
            ? s
            : "";

    public static SchemaTypeIconInfo? TypeIcon(JsonObject obj)
    {
        if (!obj.TryGetPropertyValue("icon", out JsonNode? node) || node is null)
            return null;
        if (node is not JsonObject icon)
            return new SchemaTypeIconInfo("unknown", null, null, null, null);
        if (icon.Count == 0)
            return null;

        return new SchemaTypeIconInfo(
            Str(icon, "format") is { Length: > 0 } format ? format : "unknown",
            Str(icon, "name") is { Length: > 0 } name ? name : null,
            Str(icon, "color") is { Length: > 0 } color ? color : null,
            Str(icon, "emoji") is { Length: > 0 } emoji ? emoji : null,
            Str(icon, "file") is { Length: > 0 } file ? file : null);
    }

    public static IReadOnlyList<SchemaPropertyLinkInfo> PropertyLinks(JsonObject obj)
    {
        if (obj["properties"] is not JsonArray arr) return [];
        var result = new List<SchemaPropertyLinkInfo>();
        foreach (JsonNode? node in arr)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out string? s) && !string.IsNullOrEmpty(s))
                result.Add(new SchemaPropertyLinkInfo(s, s, "", ""));
            else if (node is JsonObject p)
            {
                string id = Str(p, "id");
                string key = Str(p, "key");
                string name = Str(p, "name");
                string format = Str(p, "format");
                result.Add(new SchemaPropertyLinkInfo(id, key, name, format));
            }
        }
        return result;
    }

    // A type's description lives on its OBJECT face — the REST type surface has
    // no description field in either direction — as the entry keyed
    // "description" in the object's properties array.
    public static string ObjectDescription(JsonObject obj)
    {
        if (obj["properties"] is not JsonArray props) return "";
        foreach (JsonNode? node in props)
            if (node is JsonObject p && Str(p, "key") == "description")
                return Str(p, "text");
        return "";
    }
}
