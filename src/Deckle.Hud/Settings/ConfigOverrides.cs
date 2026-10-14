using System.Text.Json;
using System.Text.Json.Nodes;

namespace Deckle.Hud;

// A HUD config is persisted as the set of fields that differ from its compiled
// defaults, nothing more: a field left alone is absent from the file and keeps
// following the default when the default moves in code. The serializer does
// the field walk, so a property added to a config is covered without touching
// this file; a property renamed or removed leaves a stale key in the file,
// which Apply ignores.
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
        foreach (var (key, value) in overrides)
        {
            if (!node.ContainsKey(key)) continue;
            node[key] = value?.DeepClone();
        }
        return JsonSerializer.Deserialize<T>(node, options)!;
    }
}
