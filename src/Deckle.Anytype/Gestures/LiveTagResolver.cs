using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Resolves a select/multi_select value (display name or key) against a
// property's LIVE options in the space, for properties that carry NO frozen
// vocabulary in DevSpace (e.g. « tag », a free multi_select the space manages).
//
// The library must be UNABLE to create a new tag option: minting one is
// reserved to the owner, by hand, in Anytype. For a frozen vocabulary DevSpace
// enforces this in memory. For a free vocabulary this fetches the property's
// existing options; a value that matches none THROWS (listing the valid
// options) and never reaches the wire, so the API is never asked to
// auto-create one.
//
// Lifetime: one instance per gesture instance, not shared across threads. The
// key→id map is cached for the instance (a single update may touch several
// properties); option lists are read fresh each call so the resolver always
// sees the owner's latest hand-curated set.
public sealed class LiveTagResolver(AnytypeApiClient api)
{
    const int PageLimit = 100;

    readonly AnytypeApiClient _api = api;

    Dictionary<string, string>? _propertyIds;

    // A miss throws the same model-facing ArgumentException as
    // DevSpace.ResolveTag, so a free-vocabulary miss reads identically to a
    // frozen-vocabulary miss.
    public async Task<string> ResolveAsync(string propKey, string nameOrKey, CancellationToken ct = default)
    {
        string propertyId = await ResolvePropertyIdAsync(propKey, ct).ConfigureAwait(false);
        IReadOnlyList<(string Key, string Name)> options =
            await ListOptionsAsync(propertyId, ct).ConfigureAwait(false);

        foreach ((string key, string name) in options)
            if (string.Equals(key, nameOrKey, StringComparison.Ordinal) ||
                string.Equals(name, nameOrKey, StringComparison.OrdinalIgnoreCase))
                return key;

        string valid = options.Count == 0
            ? "aucune"
            : string.Join(", ", options.Select(o => $"{o.Name} ({o.Key})"));
        throw new ArgumentException(
            $"Valeur « {nameOrKey} » inconnue pour « {propKey} ». Options : {valid}.",
            nameof(nameOrKey));
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    async Task<string> ResolvePropertyIdAsync(string propKey, CancellationToken ct)
    {
        _propertyIds ??= await LoadPropertyIdsAsync(ct).ConfigureAwait(false);

        if (_propertyIds.TryGetValue(propKey, out string? id))
            return id;

        throw new InvalidOperationException(
            $"Propriété « {propKey} » introuvable dans l'espace : ses options ne peuvent être résolues.");
    }

    async Task<Dictionary<string, string>> LoadPropertyIdsAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (JsonObject root in PagedRead.PagesAsync(
            "property_ids", (offset, token) => _api.ListPropertiesAsync(offset, PageLimit, token), ct).ConfigureAwait(false))
        {
            foreach (JsonNode? node in root["data"]?.AsArray() ?? [])
            {
                if (node is not JsonObject p) continue;
                string? key = p["key"]?.GetValue<string>();
                string? id = p["id"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(id))
                    map[key] = id;
            }
        }
        return map;
    }

    async Task<IReadOnlyList<(string Key, string Name)>> ListOptionsAsync(
        string propertyId, CancellationToken ct)
    {
        var options = new List<(string, string)>();
        await foreach (JsonObject root in PagedRead.PagesAsync(
            "property_options", (offset, token) => _api.ListPropertyTagsAsync(propertyId, offset, PageLimit, token), ct)
            .ConfigureAwait(false))
        {
            foreach (JsonNode? node in root["data"]?.AsArray() ?? [])
            {
                if (node is not JsonObject t) continue;
                string? key = t["key"]?.GetValue<string>();
                if (string.IsNullOrEmpty(key)) continue;
                string name = t["name"]?.GetValue<string>() ?? key;
                options.Add((key, name));
            }
        }
        return options;
    }
}
