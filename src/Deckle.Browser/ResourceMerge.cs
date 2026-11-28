using System.Text.Json.Nodes;

namespace Deckle.Browser;

// The engine holds one array of resources (loading a second replaces the first),
// and refuses a name or an alias it already holds. uBlock Origin's scriptlets are
// the ones its own lists are written for, so they go first and win: a Brave
// resource that takes a name or an alias of theirs is dropped.
public static class ResourceMerge
{
    /// <summary>The two arrays of resources as one JSON array, the first one's entries
    /// kept whole and the second's entries kept unless one of their names or aliases
    /// is already taken. A text that is not a JSON array counts as empty.</summary>
    public static string Merge(string winning, string other)
    {
        var result = new JsonArray();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? entry in Entries(winning).Concat(Entries(other)))
        {
            if (entry is not JsonObject resource) continue;
            string[] names = Names(resource).ToArray();
            if (names.Length == 0 || names.Any(taken.Contains)) continue;
            foreach (string name in names) taken.Add(name);
            result.Add(resource.DeepClone());
        }
        return result.ToJsonString();
    }

    private static IEnumerable<JsonNode?> Entries(string json)
    {
        try { return JsonNode.Parse(json) is JsonArray array ? array.ToArray() : []; }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private static IEnumerable<string> Names(JsonObject resource)
    {
        if (resource["name"] is JsonValue name && name.TryGetValue(out string? n) && n.Length > 0) yield return n;
        else yield break;
        if (resource["aliases"] is JsonArray aliases)
            foreach (JsonNode? alias in aliases)
                if (alias is JsonValue v && v.TryGetValue(out string? a) && a.Length > 0) yield return a;
    }
}
