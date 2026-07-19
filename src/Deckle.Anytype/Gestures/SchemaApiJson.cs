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
}
