using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype;

// Resolves a select/multi_select value (display name or key) against a
// property's LIVE options in the space, for properties that carry NO frozen
// vocabulary in DevSpace (e.g. « tag », a free multi_select the space manages).
//
// Why it exists — the owner's guarantee: the library must be UNABLE to create a
// new tag option. Applying an existing option is the whole point; minting one is
// reserved to the owner, by hand, in Anytype. For a frozen vocabulary DevSpace
// enforces this in memory. For a free vocabulary there is nothing to match in
// memory, so this fetches the property's existing options and resolves against
// them; a value that matches none THROWS (listing the valid options) and never
// reaches the wire. The value is therefore guaranteed to name an option that
// already exists — the API is never asked to auto-create one.
//
// Endpoints (verified against the live API): the option list lives at
// GET /v1/spaces/{space}/properties/{property_id}/tags and takes the property
// ID, not its key — so the resolver maps key→id through GET .../properties
// first. Both are paginated ({data, pagination.has_more}).
//
// Lifetime: one instance per gesture instance, not shared across threads. The
// key→id map is cached for the instance (a single update may touch several
// properties); option lists are read fresh each call so the resolver always
// sees the owner's latest hand-curated set.
public sealed class LiveTagResolver(AnytypeApiClient api)
{
    readonly AnytypeApiClient _api = api;

    // key → property id, filled lazily on first need and reused for the instance.
    Dictionary<string, string>? _propertyIds;

    // Resolves nameOrKey to the wire key of an EXISTING option on propKey.
    // Throws ArgumentException (listing the valid options) when nothing matches —
    // the same model-facing shape as DevSpace.ResolveTag, so a free-vocabulary
    // miss reads identically to a frozen-vocabulary miss. The unresolved value is
    // never returned, so it can never reach a POST/PATCH.
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

    // The property's id for the given key, from the space's property list.
    // Throws when the key names no property — a schema/space mismatch, surfaced
    // rather than silently skipped.
    async Task<string> ResolvePropertyIdAsync(string propKey, CancellationToken ct)
    {
        _propertyIds ??= await LoadPropertyIdsAsync(ct).ConfigureAwait(false);

        if (_propertyIds.TryGetValue(propKey, out string? id))
            return id;

        throw new InvalidOperationException(
            $"Propriété « {propKey} » introuvable dans l'espace : ses options ne peuvent être résolues.");
    }

    // Walks the paginated property list once, building key→id.
    async Task<Dictionary<string, string>> LoadPropertyIdsAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        int offset = 0;
        const int limit = 100;
        while (true)
        {
            JsonObject root = await _api.ListPropertiesAsync(offset, limit, ct).ConfigureAwait(false);
            JsonArray data = root["data"]?.AsArray() ?? [];
            foreach (JsonNode? node in data)
            {
                if (node is not JsonObject p) continue;
                string? key = p["key"]?.GetValue<string>();
                string? id = p["id"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(id))
                    map[key] = id;
            }
            if (!HasMore(root)) break;
            offset += limit;
        }
        return map;
    }

    // The property's existing options as (key, name) pairs, across all pages.
    async Task<IReadOnlyList<(string Key, string Name)>> ListOptionsAsync(
        string propertyId, CancellationToken ct)
    {
        var options = new List<(string, string)>();
        int offset = 0;
        const int limit = 100;
        while (true)
        {
            JsonObject root = await _api.ListPropertyTagsAsync(propertyId, offset, limit, ct).ConfigureAwait(false);
            JsonArray data = root["data"]?.AsArray() ?? [];
            foreach (JsonNode? node in data)
            {
                if (node is not JsonObject t) continue;
                string? key = t["key"]?.GetValue<string>();
                if (string.IsNullOrEmpty(key)) continue;
                string name = t["name"]?.GetValue<string>() ?? key;
                options.Add((key, name));
            }
            if (!HasMore(root)) break;
            offset += limit;
        }
        return options;
    }

    static bool HasMore(JsonObject root) =>
        root["pagination"]?["has_more"]?.GetValue<bool>() ?? false;
}
