using System.Text.Json.Nodes;

namespace Deckle.Anytype;

public sealed class SchemaSnapshotReader(AnytypeApiClient api)
{
    private const int PageLimit = 100;
    private readonly AnytypeApiClient _api = api;

    internal async Task<SchemaSnapshot> BuildAsync(
        string spaceId,
        SchemaManifest? manifest,
        CancellationToken ct)
    {
        SchemaSnapshot snapshot = await ReadAsync(
            spaceId,
            manifest?.Properties
                .Where(p => SchemaPlanner.IsTagFormat(p.Format))
                .Select(p => p.Key)
                .ToArray(),
            ct).ConfigureAwait(false);
        return manifest is null
            ? snapshot
            : await WithTypeDescriptionsAsync(spaceId, manifest, snapshot, ct).ConfigureAwait(false);
    }

    // The types surface never returns a description, so the live value comes
    // from each type's OBJECT face — one bounded GetObject per manifest type
    // that declares one.
    private async Task<SchemaSnapshot> WithTypeDescriptionsAsync(
        string spaceId, SchemaManifest manifest, SchemaSnapshot snapshot, CancellationToken ct)
    {
        Dictionary<string, SchemaTypeInfo>? types = null;
        foreach (TypeSpec spec in manifest.Types)
        {
            if (spec.Description is null) continue;
            if (!snapshot.Types.TryGetValue(spec.Key, out SchemaTypeInfo? type) || type.Id.Length == 0)
                continue;

            JsonObject obj = await _api.GetObjectAsync(spaceId, type.Id, ct).ConfigureAwait(false);
            types ??= new Dictionary<string, SchemaTypeInfo>(snapshot.Types, StringComparer.Ordinal);
            types[spec.Key] = type with { Description = SchemaApiJson.ObjectDescription(obj) };
        }
        return types is null
            ? snapshot
            : new SchemaSnapshot(types, snapshot.Properties, snapshot.TagsByProperty);
    }

    // Public provider boundary for domains backed by Anytype. Reads the complete
    // type/property shape and, when requested, the live options of selected
    // select properties. Callers name property keys, never Anytype ids.
    public async Task<SchemaSnapshot> ReadAsync(
        string spaceId,
        IReadOnlyCollection<string>? tagPropertyKeys = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);

        Dictionary<string, SchemaTypeInfo> types = await ReadAllTypesAsync(spaceId, ct);
        Dictionary<string, SchemaPropertyInfo> properties = await ReadAllPropertiesAsync(spaceId, ct);
        var tagsByProperty = new Dictionary<string, IReadOnlyDictionary<string, SchemaTagInfo>>(StringComparer.Ordinal);

        if (tagPropertyKeys is not null)
            foreach (string key in tagPropertyKeys.Distinct(StringComparer.Ordinal))
                if (properties.TryGetValue(key, out SchemaPropertyInfo? property) && property.Id.Length > 0)
                    tagsByProperty[key] = await ReadAllTagsAsync(spaceId, property.Id, ct);

        return new SchemaSnapshot(types, properties, tagsByProperty);
    }

    private async Task<Dictionary<string, SchemaTypeInfo>> ReadAllTypesAsync(
        string spaceId, CancellationToken ct)
    {
        var result = new Dictionary<string, SchemaTypeInfo>(StringComparer.Ordinal);
        int offset = 0;
        while (true)
        {
            JsonObject root = await _api.ListTypesAsync(spaceId, offset, PageLimit, ct).ConfigureAwait(false);
            foreach (var (key, type) in ReadTypes(root)) result[key] = type;
            if (!HasMore(root)) break;
            offset += PageLimit;
        }
        return result;
    }

    private async Task<Dictionary<string, SchemaPropertyInfo>> ReadAllPropertiesAsync(
        string spaceId, CancellationToken ct)
    {
        var result = new Dictionary<string, SchemaPropertyInfo>(StringComparer.Ordinal);
        int offset = 0;
        while (true)
        {
            JsonObject root = await _api.ListPropertiesForSpaceAsync(spaceId, offset, PageLimit, ct)
                .ConfigureAwait(false);
            foreach (var (key, prop) in ReadProperties(root)) result[key] = prop;
            if (!HasMore(root)) break;
            offset += PageLimit;
        }
        return result;
    }

    private async Task<Dictionary<string, SchemaTagInfo>> ReadAllTagsAsync(
        string spaceId, string propertyId, CancellationToken ct)
    {
        var result = new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);
        int offset = 0;
        while (true)
        {
            JsonObject root = await _api.ListPropertyTagsForSpaceAsync(spaceId, propertyId, offset, PageLimit, ct)
                .ConfigureAwait(false);
            foreach (var (key, tag) in ReadTags(root)) result[key] = tag;
            if (!HasMore(root)) break;
            offset += PageLimit;
        }
        return result;
    }

    private static Dictionary<string, SchemaTypeInfo> ReadTypes(JsonObject root)
    {
        var result = new Dictionary<string, SchemaTypeInfo>(StringComparer.Ordinal);
        foreach (JsonObject obj in SchemaApiJson.Data(root))
        {
            string key = SchemaApiJson.Str(obj, "key");
            if (key.Length == 0) continue;

            result[key] = new SchemaTypeInfo(
                SchemaApiJson.Id(obj),
                key,
                SchemaApiJson.Str(obj, "name"),
                SchemaApiJson.Str(obj, "plural_name"),
                SchemaApiJson.Str(obj, "layout"),
                SchemaApiJson.TypeIcon(obj),
                SchemaApiJson.PropertyLinks(obj));
        }
        return result;
    }

    private static Dictionary<string, SchemaPropertyInfo> ReadProperties(JsonObject root)
    {
        var result = new Dictionary<string, SchemaPropertyInfo>(StringComparer.Ordinal);
        foreach (JsonObject obj in SchemaApiJson.Data(root))
        {
            string key = SchemaApiJson.Str(obj, "key");
            if (key.Length == 0) continue;

            result[key] = new SchemaPropertyInfo(
                SchemaApiJson.Id(obj),
                key,
                SchemaApiJson.Str(obj, "name"),
                SchemaApiJson.Str(obj, "format"));
        }
        return result;
    }

    private static Dictionary<string, SchemaTagInfo> ReadTags(JsonObject root)
    {
        var result = new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);
        foreach (JsonObject obj in SchemaApiJson.Data(root))
        {
            string key = SchemaApiJson.Str(obj, "key");
            string name = SchemaApiJson.Str(obj, "name");

            var tag = new SchemaTagInfo(SchemaApiJson.Id(obj), key, name, SchemaApiJson.Str(obj, "color"));
            if (key.Length > 0) result[key] = tag;
            if (name.Length > 0) result[name] = tag;
        }
        return result;
    }


    private static bool HasMore(JsonObject root) =>
        root["pagination"]?["has_more"]?.GetValue<bool>() ?? false;
}
