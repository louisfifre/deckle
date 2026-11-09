using System.Net.Http;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

// The few v2 calls the structure export, write and read-back make, over the
// public pass-through of the v2 client. Space-relative paths, JSON in and out;
// a refusal is the client's AnytypeV2Exception (an HttpRequestException), left
// for the caller to count. Nothing here knows a Home key.
internal sealed class HomeV2(AnytypeApiClient api, string spaceId)
{
    private const int PageLimit = 100;

    public string SpaceId { get; } = spaceId;

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct) =>
        (await api.V2.SendJsonAsync(HttpMethod.Get, path, null, null, SpaceId, ct).ConfigureAwait(false)).Body;

    public async Task<JsonNode?> PatchAsync(string path, JsonNode body, CancellationToken ct) =>
        (await api.V2.SendJsonAsync(HttpMethod.Patch, path, body, null, SpaceId, ct).ConfigureAwait(false)).Body;

    public async Task<JsonNode?> PostAsync(
        string path, JsonNode body, IReadOnlyList<KeyValuePair<string, string>>? query, CancellationToken ct) =>
        (await api.V2.SendJsonAsync(HttpMethod.Post, path, body, query, SpaceId, ct).ConfigureAwait(false)).Body;

    // A listing read to its end. The answer is {data:[…]} with has_more at the
    // root or under pagination, or a bare array; the next page starts after the
    // rows served, and an empty page ends the loop whatever has_more claims.
    public async Task<IReadOnlyList<JsonObject>> ListAsync(string path, CancellationToken ct)
    {
        var result = new List<JsonObject>();
        for (int offset = 0; ;)
        {
            JsonNode? root = (await api.V2.SendJsonAsync(
                HttpMethod.Get, path, null,
                [new("limit", PageLimit.ToString()), new("offset", offset.ToString())],
                SpaceId, ct).ConfigureAwait(false)).Body;

            JsonArray? rows = root as JsonArray ?? (root as JsonObject)?["data"] as JsonArray;
            int returned = 0;
            if (rows is not null)
                foreach (JsonNode? node in rows)
                {
                    returned++;
                    if (node is JsonObject row) result.Add((JsonObject)row.DeepClone());
                }

            bool more = root is JsonObject obj
                && (obj["has_more"]?.GetValue<bool>() == true || obj["pagination"]?["has_more"]?.GetValue<bool>() == true);
            if (!more || returned == 0) return result;
            offset += returned;
        }
    }

    // Type key → the type's object id, from the v1 listing the replay already
    // reads. A widget points at a type by that id.
    public async Task<Dictionary<string, string>> TypeIdsAsync(CancellationToken ct)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int offset = 0; ;)
        {
            JsonObject root = await api.ListTypesAsync(SpaceId, offset, PageLimit, ct).ConfigureAwait(false);
            int returned = 0;
            foreach (JsonObject type in (root["data"] as JsonArray ?? []).OfType<JsonObject>())
            {
                returned++;
                string key = HomeObjectJson.String(type, "key");
                string id = HomeObjectJson.String(type, "id");
                if (key.Length > 0 && id.Length > 0) ids[key] = id;
            }
            if (root["pagination"]?["has_more"]?.GetValue<bool>() != true || returned == 0) return ids;
            offset += returned;
        }
    }

    // The dataview block of a type or template document, or null.
    public static JsonObject? DataviewBlock(JsonObject document) =>
        (document["blocks"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(block => HomeObjectJson.String(block, "type") == "dataview");
}
