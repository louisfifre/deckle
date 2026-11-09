using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Reading what a collection holds. A collection IS a list in the API, and its
// members are served per view (GET /lists/{id}/views, then
// /lists/{id}/views/{view}/objects, both paged), so a reader that wants every
// member walks every view: a filtered view hides members.
//
// Route taken from the published API reference (versions 2025-05-20 and
// 2025-11-08) for the Home recreation rehearsal; its first live use is that
// rehearsal, which counts a collection it cannot read instead of failing.
public sealed partial class AnytypeApiClient
{
    public async Task<JsonObject> ListCollectionViewsAsync(
        string spaceId,
        string collectionId,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        string path = $"{SpacePath(spaceId)}/lists/{collectionId}/views?offset={offset}&limit={limit}";
        return await SendAsync(
            HttpMethod.Get, path, null, ct, replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
    }

    public async Task<JsonObject> ListCollectionObjectsAsync(
        string spaceId,
        string collectionId,
        string viewId,
        int offset = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);
        string path =
            $"{SpacePath(spaceId)}/lists/{collectionId}/views/{viewId}/objects?offset={offset}&limit={limit}";
        return await SendAsync(
            HttpMethod.Get, path, null, ct, replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
    }
}
