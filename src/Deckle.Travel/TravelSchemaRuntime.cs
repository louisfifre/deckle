using Deckle.Anytype;

namespace Deckle.Travel;

internal sealed class TravelSchemaRuntime
{
    private readonly SchemaSnapshot _snapshot;

    public TravelSchemaRuntime(SchemaSnapshot snapshot) => _snapshot = snapshot;

    public SchemaPropertyInfo Property(string key) =>
        _snapshot.Properties.TryGetValue(key, out SchemaPropertyInfo? property)
            ? property
            : throw new TravelSchemaException($"Propriété Travel absente : {key}.");

    public IReadOnlyList<SchemaPropertyInfo> PropertiesFor(string typeKey)
    {
        if (!_snapshot.Types.TryGetValue(typeKey, out SchemaTypeInfo? type))
            throw new TravelSchemaException($"Type Travel absent : {typeKey}.");

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
