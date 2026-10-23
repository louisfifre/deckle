using System.Text.Json;
using System.Text.Json.Nodes;

namespace Deckle.Hud;

// A HUD config is persisted as the set of fields that differ from its compiled
// defaults, nothing more: a field left alone is absent from the file and keeps
// following the default when the default moves in code. The serializer does
// the field walk, so a property added to a config is covered without touching
// this file; a property renamed or removed leaves a stale key in the file,
// which Apply ignores, as it does a stored value of the wrong type.
internal static class ConfigOverrides
{
    /// <summary>
    /// The fields of <paramref name="edited"/> whose value differs from
    /// <paramref name="defaults"/>, as the JSON object to persist; null when
    /// nothing differs.
    /// </summary>
    public static JsonObject? Diff<T>(T defaults, T edited, JsonSerializerOptions options)
    {
        var baseline = JsonSerializer.SerializeToNode(defaults, options)!.AsObject();
        var changed  = JsonSerializer.SerializeToNode(edited, options)!.AsObject();

        JsonObject? overrides = null;
        foreach (var (key, value) in changed)
        {
            if (JsonNode.DeepEquals(value, baseline[key])) continue;
            overrides ??= new JsonObject();
            // A node has one parent; the clone leaves `changed` intact.
            overrides[key] = value?.DeepClone();
        }
        return overrides;
    }

    /// <summary>
    /// <paramref name="defaults"/> with every field named in
    /// <paramref name="overrides"/> replaced by the stored value.
    /// </summary>
    public static T Apply<T>(T defaults, JsonObject? overrides, JsonSerializerOptions options)
    {
        if (overrides is null || overrides.Count == 0) return defaults;

        var node = JsonSerializer.SerializeToNode(defaults, options)!.AsObject();
        var known = new List<KeyValuePair<string, JsonNode?>>();
        foreach (var (key, value) in overrides)
        {
            if (!node.ContainsKey(key)) continue;
            known.Add(new(key, value));
            node[key] = value?.DeepClone();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(node, options)!;
        }
        catch (JsonException)
        {
            // A value of the wrong type (a hand edit, or a field whose type
            // changed in code). Keep the overrides that read, drop the others:
            // one bad field must not take the whole configuration with it.
            return ApplyReadableOnly(defaults, known, options);
        }
    }

    private static T ApplyReadableOnly<T>(
        T defaults, List<KeyValuePair<string, JsonNode?>> known, JsonSerializerOptions options)
    {
        var node = JsonSerializer.SerializeToNode(defaults, options)!.AsObject();
        foreach (var (key, value) in known)
        {
            JsonNode? previous = node[key]?.DeepClone();
            node[key] = value?.DeepClone();
            try
            {
                JsonSerializer.Deserialize<T>(node, options);
            }
            catch (JsonException)
            {
                node[key] = previous;
            }
        }
        return JsonSerializer.Deserialize<T>(node, options)!;
    }
}
