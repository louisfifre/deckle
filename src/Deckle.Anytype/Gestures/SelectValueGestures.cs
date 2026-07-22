using System.Text.Json.Nodes;
using System.Diagnostics;

namespace Deckle.Anytype;

// One bounded cross-space property write. Callers address live select options by
// their Anytype tag keys; display labels and provider-local ids never become the
// write contract.
public sealed class SelectValueGestures(
    AnytypeApiClient api,
    AnytypeSpaceAliases aliases,
    NameResolver resolver)
{
    private readonly SchemaSnapshotReader _schema = new(api);

    public async Task<string> SetAsync(
        string space,
        string target,
        string propertyKey,
        IReadOnlyList<string> tagKeys,
        CancellationToken ct = default)
    {
        long started = Stopwatch.GetTimestamp();
        if (string.IsNullOrWhiteSpace(propertyKey))
            throw new ArgumentException("La clé de propriété ne peut pas être vide.", nameof(propertyKey));
        if (tagKeys is null || tagKeys.Count == 0)
            throw new ArgumentException("Au moins une clé de tag est requise.", nameof(tagKeys));

        propertyKey = propertyKey.Trim();
        string[] requestedKeys = tagKeys.Select(RequiredTagKey).Distinct(StringComparer.Ordinal).ToArray();
        string spaceId = aliases.Resolve(space);
        string objectId = await resolver.ResolveAsync(spaceId, target, typeKeys: null, ct)
            .ConfigureAwait(false);

        using var _ = await api.AcquireWriteScopeAsync("select_set", spaceId, ct)
            .ConfigureAwait(false);

        await api.GetObjectAsync(spaceId, objectId, ct).ConfigureAwait(false);
        SchemaSnapshot snapshot = await _schema.ReadAsync(spaceId, [propertyKey], ct).ConfigureAwait(false);
        if (!snapshot.Properties.TryGetValue(propertyKey, out SchemaPropertyInfo? property))
            throw new InvalidOperationException(
                $"Propriété « {propertyKey} » introuvable dans l’espace {space}.");
        if (property.Format is not "select" and not "multi_select")
            throw new InvalidOperationException(
                $"La propriété « {propertyKey} » est de format {property.Format}, pas select ou multi_select.");
        if (property.Format == "select" && requestedKeys.Length != 1)
            throw new ArgumentException(
                $"La propriété select « {propertyKey} » exige exactement une clé de tag.", nameof(tagKeys));

        IReadOnlyDictionary<string, SchemaTagInfo> indexed =
            snapshot.TagsByProperty.TryGetValue(propertyKey, out var tags)
                ? tags
                : new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);
        SchemaTagInfo[] liveTags = indexed.Values
            .Where(tag => tag.Key.Length > 0)
            .DistinctBy(tag => tag.Key, StringComparer.Ordinal)
            .ToArray();

        var resolvedKeys = new List<string>(requestedKeys.Length);
        foreach (string requested in requestedKeys)
        {
            SchemaTagInfo? match = liveTags.FirstOrDefault(tag =>
                string.Equals(tag.Key, requested, StringComparison.Ordinal));
            if (match is null)
                throw new InvalidOperationException(
                    $"Clé de tag inconnue « {requested} » pour « {propertyKey} ». "
                    + "Clés présentes : " + ValidTags(liveTags) + ".");
            resolvedKeys.Add(match.Key);
        }

        JsonNode value = property.Format == "select"
            ? JsonValue.Create(resolvedKeys[0])!
            : new JsonArray(resolvedKeys.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray());
        var entry = new JsonObject
        {
            ["key"] = property.Key,
            [property.Format] = value,
        };
        await api.UpdateObjectAsync(
            spaceId,
            objectId,
            new JsonObject { ["properties"] = new JsonArray { entry } },
            ct).ConfigureAwait(false);

        DeckleAnytypeSource.Log.GestureCompleted(
            "select_set", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return $"Select mis à jour : {propertyKey} = {string.Join(", ", resolvedKeys)}.";
    }

    private static string RequiredTagKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Une clé de tag ne peut pas être vide.", nameof(value));
        return value.Trim();
    }

    private static string ValidTags(IEnumerable<SchemaTagInfo> tags)
    {
        string[] values = tags.Select(tag => $"{tag.Key} ({tag.Name})").ToArray();
        return values.Length == 0 ? "aucune" : string.Join(", ", values);
    }
}
