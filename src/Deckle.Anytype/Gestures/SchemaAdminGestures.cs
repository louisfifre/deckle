using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

public sealed class SchemaAdminGestures(AnytypeApiClient api, AnytypeSpaceAliases aliases)
{
    private const int PageLimit = 100;
    private readonly Dictionary<string, SchemaPreview> _previews = new(StringComparer.Ordinal);

    public async Task<string> InspectAsync(string space, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        string spaceId = aliases.Resolve(space);

        SchemaSnapshot snapshot = await BuildSnapshotAsync(spaceId, null, ct);

        var sb = new StringBuilder();
        sb.Append("Espace ").Append(space).Append(" : ").Append(spaceId).Append('\n');
        sb.Append("Types : ").Append(snapshot.Types.Count).Append('\n');
        foreach (TypeInfo type in snapshot.Types.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
            sb.Append("- ").Append(type.Key).Append(" · ").Append(type.Name).Append(" · ").Append(type.Layout).Append('\n');

        sb.Append("Propriétés : ").Append(snapshot.Properties.Count).Append('\n');
        foreach (PropertyInfo prop in snapshot.Properties.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
            sb.Append("- ").Append(prop.Key).Append(" · ").Append(prop.Name).Append(" · ").Append(prop.Format).Append('\n');

        DeckleAnytypeSource.Log.GestureCompleted("schema_inspect_space", Elapsed(started));
        return sb.ToString().TrimEnd();
    }

    public async Task<string> PreviewAsync(string space, JsonObject manifestNode, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        string spaceId = aliases.Resolve(space);
        SchemaManifest manifest = SchemaManifest.Parse(manifestNode);
        SchemaSnapshot snapshot = await BuildSnapshotAsync(spaceId, manifest, ct);
        SchemaPreview preview = BuildPreview(space, spaceId, manifest, snapshot);

        _previews[preview.Id] = preview;

        DeckleAnytypeSource.Log.GestureCompleted("schema_preview", Elapsed(started));
        return RenderPreview(preview);
    }

    public async Task<string> ApplyAsync(
        string space, string previewId, bool confirm, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        if (!confirm)
            throw new InvalidOperationException(
                "schema_apply exige confirm:true avec un preview_id relu juste avant.");

        if (!_previews.TryGetValue(previewId, out SchemaPreview? preview))
            throw new InvalidOperationException(
                $"Preview inconnu « {previewId} ». Relance schema_preview.");

        if (!string.Equals(preview.SpaceAlias, space, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Le preview « {previewId} » cible l'espace {preview.SpaceAlias}, pas {space}.");

        if (preview.Conflicts.Count > 0)
            throw new InvalidOperationException(
                "Preview avec conflit : schema_apply refusé. Corrige le manifeste puis relance schema_preview.");

        using var _ = await api.AcquireWriteScopeAsync("schema_apply", preview.SpaceId, ct);

        SchemaSnapshot snapshot = await BuildSnapshotAsync(preview.SpaceId, preview.Manifest, ct);
        SchemaPreview livePlan = BuildPreview(preview.SpaceAlias, preview.SpaceId, preview.Manifest, snapshot);
        if (livePlan.Conflicts.Count > 0)
            throw new InvalidOperationException(
                "L'état Anytype a changé depuis le preview et produit maintenant un conflit. Relance schema_preview.");
        EnsureNoUnpreviewedActions(preview, livePlan);

        var propertyIds = snapshot.Properties.ToDictionary(
            p => p.Key, p => p.Value.Id, StringComparer.Ordinal);
        var typeIds = snapshot.Types.ToDictionary(
            t => t.Key, t => t.Value.Id, StringComparer.Ordinal);

        var applied = new List<string>();

        foreach (PropertySpec spec in preview.Manifest.Properties)
        {
            if (propertyIds.ContainsKey(spec.Key)) continue;

            JsonObject created = await api.CreatePropertyAsync(
                preview.SpaceId,
                new JsonObject
                {
                    ["key"] = spec.Key,
                    ["name"] = spec.Name,
                    ["format"] = spec.Format,
                },
                ct);

            string id = JsonObj.Id(created);
            if (id.Length > 0)
                propertyIds[spec.Key] = id;
            applied.Add($"propriété créée {spec.Key}");
        }

        foreach (PropertySpec spec in preview.Manifest.Properties)
        {
            if (!IsTagFormat(spec.Format) || spec.Tags.Count == 0) continue;
            if (!propertyIds.TryGetValue(spec.Key, out string? propertyId) || propertyId.Length == 0)
                throw new InvalidOperationException(
                    $"Impossible de créer les tags de « {spec.Key} » : id de propriété introuvable.");

            IReadOnlyDictionary<string, TagInfo> existingTags =
                snapshot.TagsByProperty.TryGetValue(spec.Key, out var tags)
                    ? tags
                    : new Dictionary<string, TagInfo>(StringComparer.Ordinal);

            foreach (TagSpec tag in spec.Tags)
            {
                if (HasTag(existingTags, tag)) continue;

                var payload = new JsonObject
                {
                    ["name"] = tag.Name,
                    ["color"] = tag.Color ?? "grey",
                };
                await api.CreatePropertyTagAsync(preview.SpaceId, propertyId, payload, ct);
                applied.Add($"tag créé {spec.Key}:{tag.Name}");
            }
        }

        foreach (TypeSpec spec in preview.Manifest.Types)
        {
            if (typeIds.ContainsKey(spec.Key)) continue;

            JsonObject created = await api.CreateTypeAsync(
                preview.SpaceId,
                new JsonObject
                {
                    ["key"] = spec.Key,
                    ["name"] = spec.Name,
                    ["layout"] = spec.Layout,
                },
                ct);

            string id = JsonObj.Id(created);
            if (id.Length > 0)
                typeIds[spec.Key] = id;
            applied.Add($"type créé {spec.Key}");
        }

        foreach (TypeSpec type in preview.Manifest.Types)
        {
            if (type.Properties.Count == 0) continue;
            if (!typeIds.TryGetValue(type.Key, out string? typeId) || typeId.Length == 0)
                throw new InvalidOperationException(
                    $"Impossible d'attacher les propriétés à « {type.Key} » : id de type introuvable.");

            var refs = new List<string>();
            string liveTypeName = type.Name;
            if (snapshot.Types.TryGetValue(type.Key, out TypeInfo? existingType))
            {
                refs.AddRange(existingType.PropertyRefs);
                if (existingType.Name.Length > 0)
                    liveTypeName = existingType.Name;
            }

            bool changed = false;
            foreach (string propKey in type.Properties)
            {
                if (!propertyIds.TryGetValue(propKey, out string? propertyId) || propertyId.Length == 0)
                    throw new InvalidOperationException(
                        $"Propriété « {propKey} » introuvable pour le type « {type.Key} ».");

                if (!refs.Contains(propertyId) && !refs.Contains(propKey))
                {
                    refs.Add(propertyId);
                    changed = true;
                }
            }

            if (!changed) continue;

            var properties = new JsonArray();
            foreach (string propRef in refs) properties.Add(propRef);
            await api.UpdateTypeAsync(
                preview.SpaceId,
                typeId,
                new JsonObject
                {
                    ["name"] = liveTypeName,
                    ["properties"] = properties,
                },
                ct);
            applied.Add($"propriétés attachées à {type.Key}");
        }

        _previews.Remove(previewId);

        DeckleAnytypeSource.Log.GestureCompleted("schema_apply", Elapsed(started));
        return applied.Count == 0
            ? $"Schéma inchangé : preview {previewId} ne contenait rien à appliquer."
            : "Schéma appliqué :\n" + string.Join("\n", applied.Select(a => "- " + a));
    }

    private async Task<SchemaSnapshot> BuildSnapshotAsync(
        string spaceId,
        SchemaManifest? manifest,
        CancellationToken ct)
    {
        Dictionary<string, TypeInfo> types = await ReadAllTypesAsync(spaceId, ct);
        Dictionary<string, PropertyInfo> properties = await ReadAllPropertiesAsync(spaceId, ct);
        var tagsByProperty = new Dictionary<string, IReadOnlyDictionary<string, TagInfo>>(StringComparer.Ordinal);

        if (manifest is not null)
            foreach (PropertySpec spec in manifest.Properties.Where(p => IsTagFormat(p.Format)))
                if (properties.TryGetValue(spec.Key, out PropertyInfo? property) && property.Id.Length > 0)
                    tagsByProperty[spec.Key] = await ReadAllTagsAsync(spaceId, property.Id, ct);

        return new SchemaSnapshot(types, properties, tagsByProperty);
    }

    private async Task<Dictionary<string, TypeInfo>> ReadAllTypesAsync(
        string spaceId, CancellationToken ct)
    {
        var result = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
        int offset = 0;
        while (true)
        {
            JsonObject root = await api.ListTypesAsync(spaceId, offset, PageLimit, ct).ConfigureAwait(false);
            foreach (var (key, type) in ReadTypes(root)) result[key] = type;
            if (!HasMore(root)) break;
            offset += PageLimit;
        }
        return result;
    }

    private async Task<Dictionary<string, PropertyInfo>> ReadAllPropertiesAsync(
        string spaceId, CancellationToken ct)
    {
        var result = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        int offset = 0;
        while (true)
        {
            JsonObject root = await api.ListPropertiesForSpaceAsync(spaceId, offset, PageLimit, ct)
                .ConfigureAwait(false);
            foreach (var (key, prop) in ReadProperties(root)) result[key] = prop;
            if (!HasMore(root)) break;
            offset += PageLimit;
        }
        return result;
    }

    private async Task<Dictionary<string, TagInfo>> ReadAllTagsAsync(
        string spaceId, string propertyId, CancellationToken ct)
    {
        var result = new Dictionary<string, TagInfo>(StringComparer.Ordinal);
        int offset = 0;
        while (true)
        {
            JsonObject root = await api.ListPropertyTagsForSpaceAsync(spaceId, propertyId, offset, PageLimit, ct)
                .ConfigureAwait(false);
            foreach (var (key, tag) in ReadTags(root)) result[key] = tag;
            if (!HasMore(root)) break;
            offset += PageLimit;
        }
        return result;
    }

    private static Dictionary<string, TypeInfo> ReadTypes(JsonObject root)
    {
        var result = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
        foreach (JsonObject obj in JsonObj.Data(root))
        {
            string key = JsonObj.Str(obj, "key");
            if (key.Length == 0) continue;

            result[key] = new TypeInfo(
                JsonObj.Id(obj),
                key,
                JsonObj.Str(obj, "name"),
                JsonObj.Str(obj, "layout"),
                JsonObj.PropertyRefs(obj));
        }
        return result;
    }

    private static Dictionary<string, PropertyInfo> ReadProperties(JsonObject root)
    {
        var result = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (JsonObject obj in JsonObj.Data(root))
        {
            string key = JsonObj.Str(obj, "key");
            if (key.Length == 0) continue;

            result[key] = new PropertyInfo(
                JsonObj.Id(obj),
                key,
                JsonObj.Str(obj, "name"),
                JsonObj.Str(obj, "format"));
        }
        return result;
    }

    private static Dictionary<string, TagInfo> ReadTags(JsonObject root)
    {
        var result = new Dictionary<string, TagInfo>(StringComparer.Ordinal);
        foreach (JsonObject obj in JsonObj.Data(root))
        {
            string key = JsonObj.Str(obj, "key");
            string name = JsonObj.Str(obj, "name");

            var tag = new TagInfo(JsonObj.Id(obj), key, name, JsonObj.Str(obj, "color"));
            if (key.Length > 0) result[key] = tag;
            if (name.Length > 0) result[name] = tag;
        }
        return result;
    }

    private static SchemaPreview BuildPreview(
        string spaceAlias,
        string spaceId,
        SchemaManifest manifest,
        SchemaSnapshot snapshot)
    {
        var actions = new List<SchemaAction>();
        var conflicts = new List<string>();

        foreach (PropertySpec prop in manifest.Properties)
        {
            if (snapshot.Properties.TryGetValue(prop.Key, out PropertyInfo? existing))
            {
                if (!string.Equals(existing.Format, prop.Format, StringComparison.Ordinal))
                    conflicts.Add(
                        $"propriété {prop.Key} : format existant {existing.Format}, demandé {prop.Format}");
            }
            else
            {
                actions.Add(new SchemaAction("create_property", prop.Key, prop.Name));
            }

            if (IsTagFormat(prop.Format))
            {
                snapshot.TagsByProperty.TryGetValue(prop.Key, out var tags);
                tags ??= new Dictionary<string, TagInfo>(StringComparer.Ordinal);
                foreach (TagSpec tag in prop.Tags)
                    if (!HasTag(tags, tag))
                        actions.Add(new SchemaAction("create_tag", $"{prop.Key}:{tag.MatchKey}", tag.Name));
            }
        }

        foreach (TypeSpec type in manifest.Types)
        {
            if (!snapshot.Types.ContainsKey(type.Key))
                actions.Add(new SchemaAction("create_type", type.Key, type.Name));

            foreach (string propKey in type.Properties)
            {
                if (!manifest.Properties.Any(p => p.Key == propKey) && !snapshot.Properties.ContainsKey(propKey))
                {
                    conflicts.Add($"type {type.Key} : propriété demandée inconnue {propKey}");
                    continue;
                }

                bool alreadyAttached = snapshot.Types.TryGetValue(type.Key, out TypeInfo? existing)
                    && IsPropertyAttached(existing, snapshot, propKey);
                if (!alreadyAttached)
                    actions.Add(new SchemaAction("attach_property", $"{type.Key}:{propKey}", propKey));
            }
        }

        return new SchemaPreview(
            Id: PreviewId(),
            SpaceAlias: spaceAlias,
            SpaceId: spaceId,
            Manifest: manifest,
            Snapshot: snapshot,
            Actions: actions,
            Conflicts: conflicts);
    }

    private static string RenderPreview(SchemaPreview preview)
    {
        var sb = new StringBuilder();
        sb.Append("Preview ").Append(preview.Id)
            .Append(" · espace ").Append(preview.SpaceAlias)
            .Append(" · ").Append(preview.SpaceId).Append('\n');

        if (preview.Conflicts.Count > 0)
        {
            sb.Append("Conflits :\n");
            foreach (string conflict in preview.Conflicts)
                sb.Append("- ").Append(conflict).Append('\n');
        }

        if (preview.Actions.Count == 0)
        {
            sb.Append("Aucune création additive nécessaire.");
            return sb.ToString().TrimEnd();
        }

        sb.Append("Actions additives :\n");
        foreach (SchemaAction action in preview.Actions)
            sb.Append("- ").Append(action.Kind).Append(" · ").Append(action.Key).Append('\n');

        sb.Append("Relire puis appeler schema_apply avec confirm:true et preview_id:")
            .Append(preview.Id).Append('.');
        return sb.ToString().TrimEnd();
    }

    private static bool IsTagFormat(string format) =>
        string.Equals(format, "select", StringComparison.Ordinal)
        || string.Equals(format, "multi_select", StringComparison.Ordinal);

    private static bool HasTag(IReadOnlyDictionary<string, TagInfo> tags, TagSpec tag) =>
        tags.ContainsKey(tag.MatchKey)
        || tags.Values.Any(existing =>
            string.Equals(existing.Name, tag.Name, StringComparison.OrdinalIgnoreCase));

    private static bool IsPropertyAttached(TypeInfo type, SchemaSnapshot snapshot, string propKey)
    {
        if (type.PropertyRefs.Contains(propKey)) return true;
        return snapshot.Properties.TryGetValue(propKey, out PropertyInfo? prop)
            && prop.Id.Length > 0
            && type.PropertyRefs.Contains(prop.Id);
    }

    private static void EnsureNoUnpreviewedActions(SchemaPreview preview, SchemaPreview livePlan)
    {
        var previewed = preview.Actions
            .Select(a => $"{a.Kind}:{a.Key}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (SchemaAction action in livePlan.Actions)
            if (!previewed.Contains($"{action.Kind}:{action.Key}"))
                throw new InvalidOperationException(
                    "L'état Anytype a changé depuis le preview. Relance schema_preview avant schema_apply.");
    }

    private static bool HasMore(JsonObject root) =>
        root["pagination"]?["has_more"]?.GetValue<bool>() ?? false;

    private static string PreviewId()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static double Elapsed(DateTime startUtc) => (DateTime.UtcNow - startUtc).TotalMilliseconds;

    private sealed record SchemaPreview(
        string Id,
        string SpaceAlias,
        string SpaceId,
        SchemaManifest Manifest,
        SchemaSnapshot Snapshot,
        IReadOnlyList<SchemaAction> Actions,
        IReadOnlyList<string> Conflicts);

    private sealed record SchemaAction(string Kind, string Key, string Name);
    private sealed record SchemaSnapshot(
        IReadOnlyDictionary<string, TypeInfo> Types,
        IReadOnlyDictionary<string, PropertyInfo> Properties,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, TagInfo>> TagsByProperty);

    private sealed record TypeInfo(
        string Id,
        string Key,
        string Name,
        string Layout,
        IReadOnlyList<string> PropertyRefs);

    private sealed record PropertyInfo(string Id, string Key, string Name, string Format);
    private sealed record TagInfo(string Id, string Key, string Name, string Color);
}

sealed record SchemaManifest(
    IReadOnlyList<TypeSpec> Types,
    IReadOnlyList<PropertySpec> Properties)
{
    public static SchemaManifest Parse(JsonObject root)
    {
        JsonShape.RequireOnly(root, ["types", "properties"], "manifest");

        var types = new List<TypeSpec>();
        if (root.TryGetPropertyValue("types", out JsonNode? typesNode) && typesNode is not null)
        {
            if (typesNode is not JsonArray typeArray)
                throw new ArgumentException("Le champ « types » doit être un tableau.");

            foreach (JsonNode? node in typeArray)
            {
                if (node is not JsonObject obj)
                    throw new ArgumentException("Chaque entrée de « types » doit être un objet.");
                types.Add(TypeSpec.Parse(obj));
            }
        }

        var properties = new List<PropertySpec>();
        if (root.TryGetPropertyValue("properties", out JsonNode? propertiesNode) && propertiesNode is not null)
        {
            if (propertiesNode is not JsonArray propArray)
                throw new ArgumentException("Le champ « properties » doit être un tableau.");

            foreach (JsonNode? node in propArray)
            {
                if (node is not JsonObject obj)
                    throw new ArgumentException("Chaque entrée de « properties » doit être un objet.");
                properties.Add(PropertySpec.Parse(obj));
            }
        }

        if (types.Count == 0 && properties.Count == 0)
            throw new ArgumentException("Le manifeste doit contenir au moins un type ou une propriété.");

        JsonShape.RequireUnique(types.Select(t => t.Key), "types.key");
        JsonShape.RequireUnique(properties.Select(p => p.Key), "properties.key");

        return new SchemaManifest(types, properties);
    }
}

sealed record TypeSpec(
    string Key,
    string Name,
    string Layout,
    IReadOnlyList<string> Properties)
{
    public static TypeSpec Parse(JsonObject obj)
    {
        JsonShape.RequireOnly(obj, ["key", "name", "layout", "properties"], "type");

        string key = RequiredKey(obj, "key");
        string name = RequiredString(obj, "name");
        string layout = OptionalString(obj, "layout") ?? "page";
        var props = new List<string>();
        if (obj.TryGetPropertyValue("properties", out JsonNode? propsNode) && propsNode is not null)
        {
            if (propsNode is not JsonArray arr)
                throw new ArgumentException($"Le champ « properties » du type « {key} » doit être un tableau.");

            foreach (JsonNode? node in arr)
            {
                if (node is not JsonValue value ||
                    !value.TryGetValue<string>(out string? prop) ||
                    prop is null)
                    throw new ArgumentException(
                        $"Chaque propriété du type « {key} » doit être une clé string.");
                props.Add(KeyRules.Validate(prop, "properties"));
            }
        }
        JsonShape.RequireUnique(props, $"type {key}.properties");
        return new TypeSpec(key, name, layout, props);
    }

    private static string RequiredKey(JsonObject obj, string name) =>
        KeyRules.Validate(RequiredString(obj, name), name);

    private static string RequiredString(JsonObject obj, string name) =>
        OptionalString(obj, name)
        ?? throw new ArgumentException($"Champ requis manquant « {name} ».");

    private static string? OptionalString(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out string? s) && !string.IsNullOrWhiteSpace(s)
            ? s.Trim()
            : null;

}

sealed record PropertySpec(
    string Key,
    string Name,
    string Format,
    IReadOnlyList<TagSpec> Tags)
{
    private static readonly HashSet<string> AllowedFormats =
        new(StringComparer.Ordinal)
        {
            "text", "number", "select", "multi_select", "date", "files",
            "checkbox", "url", "email", "phone", "objects",
        };

    public static PropertySpec Parse(JsonObject obj)
    {
        JsonShape.RequireOnly(obj, ["key", "name", "format", "tags"], "property");

        string key = RequiredKey(obj, "key");
        string name = RequiredString(obj, "name");
        string format = RequiredString(obj, "format");
        if (!AllowedFormats.Contains(format))
            throw new ArgumentException(
                $"Format inconnu « {format} » pour « {key} ». Formats : {string.Join(", ", AllowedFormats)}.");

        var tags = new List<TagSpec>();
        if (obj.TryGetPropertyValue("tags", out JsonNode? tagsNode) && tagsNode is not null)
        {
            if (tagsNode is not JsonArray arr)
                throw new ArgumentException($"Le champ « tags » de « {key} » doit être un tableau.");

            foreach (JsonNode? node in arr)
            {
                if (node is JsonValue value && value.TryGetValue<string>(out string? nameValue) && nameValue is not null)
                {
                    nameValue = nameValue.Trim();
                    if (nameValue.Length == 0)
                        throw new ArgumentException($"Chaque tag string de « {key} » doit être non vide.");
                    tags.Add(new TagSpec(nameValue, null, null));
                }
                else if (node is JsonObject tagObject)
                    tags.Add(TagSpec.Parse(tagObject));
                else
                    throw new ArgumentException(
                        $"Chaque tag de « {key} » doit être une string ou un objet.");
            }
        }
        JsonShape.RequireUnique(tags.Select(t => t.MatchKey), $"property {key}.tags");

        return new PropertySpec(key, name, format, tags);
    }

    private static string RequiredKey(JsonObject obj, string name) =>
        KeyRules.Validate(RequiredString(obj, name), name);

    private static string RequiredString(JsonObject obj, string name) =>
        OptionalString(obj, name)
        ?? throw new ArgumentException($"Champ requis manquant « {name} ».");

    private static string? OptionalString(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out string? s) && !string.IsNullOrWhiteSpace(s)
            ? s.Trim()
            : null;
}

sealed record TagSpec(string Name, string? Key, string? Color)
{
    public string MatchKey => Key ?? Name;

    public static TagSpec Parse(JsonObject obj)
    {
        JsonShape.RequireOnly(obj, ["key", "name", "color"], "tag");

        string name = obj["name"] is JsonValue nameValue
            && nameValue.TryGetValue<string>(out string? n)
            && !string.IsNullOrWhiteSpace(n)
            ? n.Trim()
            : throw new ArgumentException("Chaque tag objet doit porter un champ name.");

        string? key = obj["key"] is JsonValue keyValue
            && keyValue.TryGetValue<string>(out string? k)
            && !string.IsNullOrWhiteSpace(k)
            ? KeyRules.Validate(k.Trim(), "tag.key")
            : null;

        string? color = obj["color"] is JsonValue colorValue
            && colorValue.TryGetValue<string>(out string? c)
            && !string.IsNullOrWhiteSpace(c)
            ? c.Trim()
            : null;

        return new TagSpec(name, key, color);
    }
}

static class JsonShape
{
    public static void RequireOnly(JsonObject obj, IReadOnlyCollection<string> allowed, string owner)
    {
        foreach (string key in obj.Select(kv => kv.Key))
            if (!allowed.Contains(key, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"Champ inconnu « {key} » dans « {owner} ». Champs acceptés : {string.Join(", ", allowed)}.");
    }

    public static void RequireUnique(IEnumerable<string> keys, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
            if (!seen.Add(key))
                throw new ArgumentException($"Doublon interdit dans « {label} » : {key}.");
    }
}

static class KeyRules
{
    public static string Validate(string value, string name)
    {
        value = value.Trim();
        if (value.Length == 0 || !char.IsAsciiLetterLower(value[0]) ||
            value.Any(c => c != '_' && !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c)))
        {
            throw new ArgumentException(
                $"« {name} » doit être une clé snake_case ASCII, sans accent ni espace : {value}.");
        }
        return value;
    }
}

static class JsonObj
{
    public static IEnumerable<JsonObject> Data(JsonObject root)
    {
        if (root["data"] is not JsonArray data) yield break;
        foreach (JsonNode? node in data)
            if (node is JsonObject obj)
                yield return obj["object"] as JsonObject ?? obj;
    }

    public static string Id(JsonObject obj) => Str(obj, "id");

    public static string Str(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out string? s) && s is not null
            ? s
            : "";

    public static IReadOnlyList<string> PropertyRefs(JsonObject obj)
    {
        if (obj["properties"] is not JsonArray arr) return [];
        var result = new List<string>();
        foreach (JsonNode? node in arr)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out string? s) && !string.IsNullOrEmpty(s))
                result.Add(s);
            else if (node is JsonObject p)
            {
                string id = Str(p, "id");
                string key = Str(p, "key");
                if (id.Length > 0) result.Add(id);
                if (key.Length > 0) result.Add(key);
            }
        }
        return result;
    }
}
