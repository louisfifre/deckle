using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

internal sealed class HomeCollectionWriter(
    AnytypeApiClient api,
    string spaceId,
    HomeObjectIndex objects)
{
    public IReadOnlyList<string> Resolve(IReadOnlyList<string>? selectors)
    {
        if (selectors is null || selectors.Count == 0) return [];

        var result = new List<string>(selectors.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string selector in selectors)
        {
            JsonObject collection = objects.ResolveCollection(selector);
            string id = HomeObjectJson.Id(collection);
            if (id.Length == 0)
                throw new InvalidOperationException(
                    $"La collection « {HomeObjectIndex.Display(collection)} » ne porte pas d’id Anytype.");
            if (seen.Add(id)) result.Add(id);
        }
        return result;
    }

    public async Task AddAsync(
        IReadOnlyDictionary<string, List<string>> memberships,
        CancellationToken ct)
    {
        foreach ((string collectionId, List<string> objectIds) in memberships)
            await api.AddToCollectionAsync(spaceId, collectionId, objectIds, ct).ConfigureAwait(false);
    }

    public async Task AddAsync(
        IReadOnlyList<string> collectionIds,
        string objectId,
        CancellationToken ct)
    {
        foreach (string collectionId in collectionIds)
            await api.AddToCollectionAsync(spaceId, collectionId, [objectId], ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        IReadOnlyList<string> collectionIds,
        string objectId,
        CancellationToken ct)
    {
        foreach (string collectionId in collectionIds)
            await api.RemoveFromCollectionAsync(spaceId, collectionId, objectId, ct).ConfigureAwait(false);
    }
}
