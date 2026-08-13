using Deckle.Anytype;

namespace Deckle.Home;

internal sealed class HomeSchemaRuntime
{
    private readonly SchemaSnapshot _snapshot;

    public HomeSchemaRuntime(SchemaSnapshot snapshot, string? floorTypeKey = null)
    {
        _snapshot = snapshot;
        FloorTypeKey = floorTypeKey;
    }

    // Key of the app-created collection-layout Zone type, discovered from
    // the live snapshot; null while Louis has not created it yet.
    public string? FloorTypeKey { get; }

    public SchemaPropertyInfo Property(string key) =>
        _snapshot.Properties.TryGetValue(key, out SchemaPropertyInfo? property)
            ? property
            : throw new HomeSchemaException($"Propriété Home absente : {key}.");

    // Live Anytype id of a type, needed to address its templates. A runtime
    // coordinate read from the space, never a compiled constant.
    public string TypeId(string typeKey) =>
        _snapshot.Types.TryGetValue(typeKey, out SchemaTypeInfo? type)
            ? type.Id
            : throw new HomeSchemaException($"Type Home absent : {typeKey}.");

    public IReadOnlyList<SchemaPropertyInfo> PropertiesFor(string typeKey)
    {
        if (!_snapshot.Types.TryGetValue(typeKey, out SchemaTypeInfo? type))
            throw new HomeSchemaException($"Type Home absent : {typeKey}.");

        var result = new List<SchemaPropertyInfo>();
        foreach (SchemaPropertyLinkInfo link in type.PropertyLinks)
        {
            SchemaPropertyInfo? property = null;
            if (link.Key.Length > 0) _snapshot.Properties.TryGetValue(link.Key, out property);
            if (property is null && link.Id.Length > 0)
                property = _snapshot.Properties.Values.FirstOrDefault(candidate => candidate.Id == link.Id);
            if (property is not null && !result.Any(existing => existing.Key == property.Key))
                result.Add(property);
        }
        return result;
    }

    public SchemaPropertyInfo ResolveProperty(string typeKey, string nameOrKey)
    {
        IReadOnlyList<SchemaPropertyInfo> properties = PropertiesFor(typeKey);
        SchemaPropertyInfo[] matches = properties.Where(property =>
                string.Equals(property.Key, nameOrKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, nameOrKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Propriété inconnue « {nameOrKey} » pour {typeKey}. Connues : "
                + string.Join(", ", properties.Select(p => p.Name))),
            _ => throw new InvalidOperationException($"Propriété ambiguë « {nameOrKey} » pour {typeKey}."),
        };
    }

    public IReadOnlyDictionary<string, SchemaTagInfo> TagsFor(string propertyKey) =>
        _snapshot.TagsByProperty.TryGetValue(propertyKey, out var tags)
            ? tags
            : new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);
}
