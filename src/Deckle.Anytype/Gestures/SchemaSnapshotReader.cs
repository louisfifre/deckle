using System.Text.Json.Nodes;

namespace Deckle.Anytype;

internal sealed class SchemaSnapshotReader(AnytypeApiClient api)
{
    private const int PageLimit = 100;
    private readonly AnytypeApiClient _api = api;

    internal async Task<SchemaSnapshot> BuildAsync(
        string spaceId,
        SchemaManifest? manifest,
        CancellationToken ct)
    {
        Dictionary<string, SchemaTypeInfo> types = await ReadAllTypesAsync(spaceId, ct);
        Dictionary<string, SchemaPropertyInfo> properties = await ReadAllPropertiesAsync(spaceId, ct);
        var tagsByProperty = new Dictionary<string, IReadOnlyDictionary<string, SchemaTagInfo>>(StringComparer.Ordinal);

        if (manifest is not null)
            foreach (PropertySpec spec in manifest.Properties.Where(p => SchemaPlanner.IsTagFormat(p.Format)))
                if (properties.TryGetValue(spec.Key, out SchemaPropertyInfo? property) && property.Id.Length > 0)
                    tagsByProperty[spec.Key] = await ReadAllTagsAsync(spaceId, property.Id, ct);

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
