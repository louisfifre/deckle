using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// One space as the space-level calls report it: its id and its display name.
public sealed record AnytypeSpaceSummary(string Id, string Name);

// Space-level calls, outside any one space. They exist for maintenance: a
// rehearsal of the Home recreation needs a TEST space to rebuild into, and a
// way to see which spaces the bot identity already reaches. Deckle never
// creates its live spaces in normal operation; the person creates those in
// the app.
//
// The public API has no invite endpoint (members are read-only: GET
// /members only), so a space the bot creates is visible to the bot alone
// until the person joins it from the app.
public sealed partial class AnytypeApiClient
{
    // The documented maximum of GET /v1/spaces (default 100).
    private const int SpacePageLimit = 1000;

    // Every space the bearer reaches, walking pagination to the end.
    public async Task<IReadOnlyList<AnytypeSpaceSummary>> ListSpacesAsync(CancellationToken ct = default)
    {
        var spaces = new List<AnytypeSpaceSummary>();
        int offset = 0;
        while (true)
        {
            JsonObject root = await SendAsync(
                    HttpMethod.Get, $"/v1/spaces?offset={offset}&limit={SpacePageLimit}", null, ct,
                    replaySafety: RequestReplaySafety.Safe)
                .ConfigureAwait(false);

            int returned = 0;
            if (root["data"] is JsonArray data)
                foreach (JsonNode? node in data)
                {
                    returned++;
                    if (node is JsonObject space)
                        spaces.Add(Summary(space));
                }

            // Advance by the rows served, not the limit asked for: the server
            // may cap a page below it. An empty page ends the walk whatever
            // has_more claims, so a server that lost its place cannot loop us.
            bool hasMore = root["pagination"]?["has_more"]?.GetValue<bool>() ?? false;
            if (!hasMore || returned == 0) break;
            offset += returned;
        }
        return spaces;
    }

    // Not replayed on a 5xx: the space may already exist, and a second POST
    // would make a duplicate.
    public async Task<AnytypeSpaceSummary> CreateSpaceAsync(
        string name, string? description, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var body = new JsonObject { ["name"] = name };
        if (description is not null) body["description"] = description;

        JsonObject root = await SendAsync(HttpMethod.Post, "/v1/spaces", body, ct)
            .ConfigureAwait(false);
        return Summary(Inner(root, "space"));
    }

    private static AnytypeSpaceSummary Summary(JsonObject space)
    {
        string id = space["id"]?.GetValue<string>() ?? "";
        if (id.Length == 0)
            throw new HttpRequestException("Anytype API space entry has no \"id\".");
        return new AnytypeSpaceSummary(id, space["name"]?.GetValue<string>() ?? "");
    }
}
