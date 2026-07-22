using System.Text.Json.Nodes;
using System.Diagnostics;

namespace Deckle.Anytype;

// One bounded cross-space mutation: resolve existing objects and add them to
// one existing collection. Membership is provider structure, never an object
// relation, so this gesture deliberately owns no domain routing rules.
public sealed class CollectionMembershipGestures(
    AnytypeApiClient api,
    AnytypeSpaceAliases aliases,
    NameResolver resolver)
{
    public async Task<string> AddAsync(
        string space,
        string collection,
        IReadOnlyList<string> objects,
        CancellationToken ct = default)
    {
        long started = Stopwatch.GetTimestamp();
        if (objects is null || objects.Count == 0)
            throw new ArgumentException(
                "Au moins un objet est requis pour une appartenance à une collection.", nameof(objects));

        string spaceId = aliases.Resolve(space);
        string collectionId = await resolver.ResolveAsync(spaceId, collection, typeKeys: null, ct)
            .ConfigureAwait(false);

        using var _ = await api.AcquireWriteScopeAsync("collection_add", spaceId, ct)
            .ConfigureAwait(false);

        JsonObject collectionObject = await api.GetObjectAsync(spaceId, collectionId, ct)
            .ConfigureAwait(false);
        if (!string.Equals(Layout(collectionObject), "collection", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"« {Display(collectionObject, collection)} » n’est pas une collection Anytype. "
                + "L’appartenance à une collection est distincte d’une relation d’objet.");

        var objectIds = new List<string>(objects.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string selector in objects)
        {
            string objectId = await resolver.ResolveAsync(spaceId, selector, typeKeys: null, ct)
                .ConfigureAwait(false);
            if (!seen.Add(objectId)) continue;

            // IDs bypass search in NameResolver; GET every resolved member so an
            // invalid coordinate fails before the one membership POST.
            await api.GetObjectAsync(spaceId, objectId, ct).ConfigureAwait(false);
            objectIds.Add(objectId);
        }

        await api.AddToCollectionAsync(spaceId, collectionId, objectIds, ct).ConfigureAwait(false);
        DeckleAnytypeSource.Log.GestureCompleted(
            "collection_add", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return $"Collection mise à jour : {objectIds.Count} objet(s) ajouté(s) à {Display(collectionObject, collection)}.";
    }

    private static string Layout(JsonObject value) =>
        String(value, "layout") is { Length: > 0 } layout
            ? layout
            : value["type"]?["layout"]?.GetValue<string>() ?? "";

    private static string Display(JsonObject value, string fallback) =>
        String(value, "name") is { Length: > 0 } name ? name : fallback;

    private static string String(JsonObject value, string key) =>
        value[key] is JsonValue node && node.TryGetValue<string>(out string? text)
            ? text ?? ""
            : "";
}
