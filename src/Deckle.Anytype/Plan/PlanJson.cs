using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Comparison of a wanted JSON shape against a live one, field by field.
//
// A plan says what it cares about and nothing more, so the comparison is "the
// live document holds everything the plan states": every member of the wanted
// object must be there and equal, extras on the live side are ignored, and arrays
// are compared in order (the order of views, columns, sorts and blocks is part of
// what a plan states).
internal static class PlanJson
{
    public static List<string> Differences(JsonNode? wanted, JsonNode? live, string path = "")
    {
        var differences = new List<string>();
        Compare(wanted, live, path, differences);
        return differences;
    }

    private static void Compare(JsonNode? wanted, JsonNode? live, string path, List<string> differences)
    {
        switch (wanted)
        {
            case null:
                if (live is not null) differences.Add($"{Label(path)} : attendu vide, vu {Short(live)}");
                return;
            case JsonObject wantedObject:
                if (live is not JsonObject liveObject)
                {
                    differences.Add($"{Label(path)} : attendu un objet, vu {Short(live)}");
                    return;
                }
                foreach ((string key, JsonNode? value) in wantedObject)
                {
                    if (value is null) continue;
                    if (!liveObject.TryGetPropertyValue(key, out JsonNode? liveValue) || liveValue is null)
                    {
                        // A wanted false, empty or zero is the same as the API's
                        // omission of it.
                        if (!IsZero(value)) differences.Add($"{Label(Join(path, key))} : attendu {Short(value)}, absent");
                        continue;
                    }
                    Compare(value, liveValue, Join(path, key), differences);
                }
                return;
            case JsonArray wantedArray:
                if (live is not JsonArray liveArray)
                {
                    if (wantedArray.Count > 0) differences.Add($"{Label(path)} : attendu une liste, vu {Short(live)}");
                    return;
                }
                if (wantedArray.Count != liveArray.Count)
                {
                    differences.Add($"{Label(path)} : {wantedArray.Count} élément(s) attendu(s), {liveArray.Count} vu(s)");
                    return;
                }
                for (int i = 0; i < wantedArray.Count; i++)
                    Compare(wantedArray[i], liveArray[i], $"{path}[{i}]", differences);
                return;
            default:
                if (!ScalarEquals(wanted, live))
                    differences.Add($"{Label(path)} : attendu {Short(wanted)}, vu {Short(live)}");
                return;
        }
    }

    private static bool IsZero(JsonNode value) => value switch
    {
        JsonArray array => array.Count == 0,
        JsonObject obj => obj.Count == 0,
        JsonValue scalar when scalar.TryGetValue<bool>(out bool b) => !b,
        _ => false,
    };

    private static bool ScalarEquals(JsonNode wanted, JsonNode? live)
    {
        if (live is not JsonValue liveValue || wanted is not JsonValue wantedValue) return false;
        if (wantedValue.TryGetValue<string>(out string? ws))
            return liveValue.TryGetValue<string>(out string? ls) && string.Equals(ws, ls, StringComparison.Ordinal);
        if (wantedValue.TryGetValue<bool>(out bool wb))
            return liveValue.TryGetValue<bool>(out bool lb) && wb == lb;
        if (wantedValue.TryGetValue<double>(out double wd))
            return liveValue.TryGetValue<double>(out double ld) && wd.Equals(ld);
        return JsonNode.DeepEquals(wanted, live);
    }

    private static string Join(string path, string key) => path.Length == 0 ? key : path + "." + key;
    private static string Label(string path) => path.Length == 0 ? "(racine)" : path;
    private static string Short(JsonNode? node)
    {
        string text = node?.ToJsonString() ?? "rien";
        return text.Length > 80 ? text[..77] + "…" : text;
    }
}
