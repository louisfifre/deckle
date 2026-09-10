using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Every object of the given types in a space, read through the search endpoint
// one full page at a time until pagination.has_more turns false. The exhaustive
// read the listing gestures share — a project's tasks, a task's reports, the
// project list, a space's collections — before they filter client-side. A
// single page silently drops the least recently modified objects once the space
// outgrows it. The 2025-11-08 API also offers a property filter (`filters`);
// it is unmeasured on relation properties, so the complete read stays the
// measured path.
internal static class ObjectListing
{
    public static async Task<IReadOnlyList<JsonObject>> AllOfTypesAsync(
        AnytypeApiClient api, string spaceId, IReadOnlyList<string> typeKeys, CancellationToken ct)
    {
        var objects = new List<JsonObject>();
        int offset = 0;
        while (true)
        {
            JsonObject root = await api.SearchAsync(
                    spaceId, string.Empty, typeKeys, offset, AnytypeApiClient.SearchPageMax, ct)
                .ConfigureAwait(false);
            foreach (JsonNode? node in root["data"]?.AsArray() ?? [])
                if (node is JsonObject obj) objects.Add(obj);

            if (root["pagination"]?["has_more"]?.GetValue<bool>() != true) break;
            offset += AnytypeApiClient.SearchPageMax;
        }
        return objects;
    }
}
