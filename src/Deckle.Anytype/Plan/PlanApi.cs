using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// The few v2 calls the space plan makes, in one place: a read, a paged list, a
// write. Everything goes through the client's one gate, so the stream stays a
// single in-flight request (the desktop gateway does not stand many at once).
internal sealed class PlanApi(AnytypeV2Client client, string spaceId)
{
    private const int PageLimit = 100;

    public string SpaceId => spaceId;

    public async Task<JsonObject?> TryGetAsync(string path, CancellationToken ct)
    {
        try
        {
            return (await client.SendAsync(new AnytypeV2Request(HttpMethod.Get, path, SpaceId: spaceId), ct)
                .ConfigureAwait(false)).Body as JsonObject;
        }
        catch (AnytypeV2Exception ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<JsonObject> GetAsync(string path, CancellationToken ct) =>
        (await client.SendAsync(new AnytypeV2Request(HttpMethod.Get, path, SpaceId: spaceId), ct)
            .ConfigureAwait(false)).Body as JsonObject
        ?? throw new InvalidOperationException($"GET {path} : réponse vide.");

    public async Task<IReadOnlyList<JsonObject>> ListAsync(
        string path, IReadOnlyList<KeyValuePair<string, string>>? extraQuery, CancellationToken ct)
    {
        var rows = new List<JsonObject>();
        for (int offset = 0; ;)
        {
            var query = new List<KeyValuePair<string, string>>
            {
                new("offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new("limit", PageLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            };
            if (extraQuery is not null) query.AddRange(extraQuery);
            AnytypeV2Response response = await client.SendAsync(
                new AnytypeV2Request(HttpMethod.Get, path, Query: query, SpaceId: spaceId), ct).ConfigureAwait(false);
            JsonObject? body = response.Body as JsonObject;
            JsonArray page = body?["data"] as JsonArray ?? [];
            rows.AddRange(page.OfType<JsonObject>());
            if (body?["has_more"]?.GetValue<bool>() != true || page.Count == 0) break;
            offset += page.Count;
        }
        return rows;
    }

    // A write. The idempotency key makes a resend after a 5xx safe.
    public async Task<JsonObject> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct) =>
        (await client.SendAsync(
            new AnytypeV2Request(method, path, body, IdempotencyKey: Guid.NewGuid().ToString("N"), SpaceId: spaceId), ct)
            .ConfigureAwait(false)).Body as JsonObject ?? [];

    public Task<JsonObject> PatchOpsAsync(string path, IEnumerable<JsonObject> ops, CancellationToken ct)
    {
        var array = new JsonArray();
        foreach (JsonObject op in ops) array.Add(op);
        return SendAsync(HttpMethod.Patch, path, new JsonObject { ["ops"] = array }, ct);
    }
}
