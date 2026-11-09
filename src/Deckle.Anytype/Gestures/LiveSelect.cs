using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// A select property the model expects but the space may not carry yet (the
// slot of a task, the role of a person), read live: found by one of its keys or
// by its display name, with its options as the space holds them.
//
// Why live and not frozen in DevSpace: these properties come with the model v6
// and are created by hand in Anytype or by the recreation, so their key is not
// known in advance (a property made in the app gets a key derived from its
// name; the recreation names it in English). The gesture that writes one asks
// first whether it exists, and says so when it does not, instead of sending a
// key the server would refuse.
//
// Like LiveTagResolver, nothing here can create an option: an option comes from
// the person, in Anytype.
public sealed record LiveSelect(string Key, string Name, IReadOnlyList<(string Key, string Name)> Options)
{
    const int PageLimit = 100;

    // A key match wins over a name match, and only a select qualifies: a
    // relation named like it (the v5 « Rôle » link) is another property.
    public static async Task<LiveSelect?> FindAsync(
        AnytypeApiClient api, IReadOnlyList<string> keys, string displayName, CancellationToken ct)
    {
        (string Id, string Key, string Name)? byKey = null, byName = null;
        await foreach (JsonObject root in PagedRead.PagesAsync(
            "property_list", (offset, token) => api.ListPropertiesAsync(offset, PageLimit, token), ct).ConfigureAwait(false))
        {
            foreach (JsonNode? node in root["data"]?.AsArray() ?? [])
            {
                if (node is not JsonObject p) continue;
                string id = Str(p, "id"), key = Str(p, "key"), name = Str(p, "name");
                if (id.Length == 0 || key.Length == 0 || Str(p, "format") != "select") continue;

                if (byKey is null && keys.Contains(key, StringComparer.Ordinal)) byKey = (id, key, name);
                else if (byName is null && string.Equals(name.Trim(), displayName, StringComparison.OrdinalIgnoreCase))
                    byName = (id, key, name);
            }
        }

        if ((byKey ?? byName) is not { } found) return null;

        var options = new List<(string, string)>();
        await foreach (JsonObject root in PagedRead.PagesAsync(
            "property_options", (offset, token) => api.ListPropertyTagsAsync(found.Id, offset, PageLimit, token), ct)
            .ConfigureAwait(false))
        {
            foreach (JsonNode? node in root["data"]?.AsArray() ?? [])
                if (node is JsonObject t && Str(t, "key") is { Length: > 0 } key)
                    options.Add((key, Str(t, "name") is { Length: > 0 } name ? name : key));
        }

        return new LiveSelect(found.Key, found.Name.Length > 0 ? found.Name : displayName, options);
    }

    // The option's display name, the form a v2 write takes, matched on its key
    // or its name; null when the space has no such option.
    public string? OptionName(string nameOrKey)
    {
        foreach ((string key, string name) in Options)
            if (string.Equals(key, nameOrKey, StringComparison.Ordinal)
                || string.Equals(name, nameOrKey.Trim(), StringComparison.OrdinalIgnoreCase))
                return name;
        return null;
    }

    // The model-facing refusal of an unknown value, in the words of
    // DevSpace.ResolveTag so every select miss reads the same.
    public ArgumentException Unknown(string value, string argument) => new(
        $"Valeur « {value} » inconnue pour « {Name} ». Options : "
        + (Options.Count == 0 ? "aucune" : string.Join(", ", Options.Select(o => o.Name))) + ".",
        argument);

    static string Str(JsonObject o, string name) =>
        o[name] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
}
