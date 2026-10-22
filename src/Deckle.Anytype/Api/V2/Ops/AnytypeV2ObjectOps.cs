using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// The four ops that change an object as a whole rather than its body: its
// property values, its type, and (for a collection) its members. PATCH
// /objects/{id} only.
public static class AnytypeV2ObjectOps
{
    private const int MaxItems = 1000;

    public static AnytypeV2Op SetProperties(AnytypeV2PropertyChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return new("set_properties", AnytypeV2OpScope.Object, changes.ToJson());
    }

    // The type's key (task, page…), not its id.
    public static AnytypeV2Op SetType(string typeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        return new("set_type", AnytypeV2OpScope.Object,
            new JsonObject { ["op"] = "set_type", ["type"] = typeKey });
    }

    // Already-present ids are ignored by the server.
    public static AnytypeV2Op AddItems(IEnumerable<string> objectIds) => Items("add_items", objectIds);

    public static AnytypeV2Op RemoveItems(IEnumerable<string> objectIds) => Items("remove_items", objectIds);

    private static AnytypeV2Op Items(string kind, IEnumerable<string> objectIds)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var items = new JsonArray();
        foreach (string id in objectIds)
            items.Add(id);

        if (items.Count is < 1 or > MaxItems)
            throw new ArgumentException($"{kind} takes 1 to {MaxItems} object ids.", nameof(objectIds));

        return new(kind, AnytypeV2OpScope.Object,
            new JsonObject { ["op"] = kind, ["items"] = items });
    }
}
