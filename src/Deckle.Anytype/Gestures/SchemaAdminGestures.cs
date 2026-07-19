using System.Text;
using System.Text.Json.Nodes;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

public sealed partial class SchemaAdminGestures(AnytypeApiClient api, AnytypeSpaceAliases aliases)
{
    private readonly SchemaSnapshotReader _snapshotReader = new(api);
    private readonly SchemaPreviewStore _previewStore = new();

    public async Task<string> InspectAsync(string space, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        string spaceId = aliases.Resolve(space);

        SchemaSnapshot snapshot = await _snapshotReader.BuildAsync(spaceId, null, ct);

        var sb = new StringBuilder();
        sb.Append("Espace ").Append(space).Append(" : ").Append(spaceId).Append('\n');
        sb.Append("Types : ").Append(snapshot.Types.Count).Append('\n');
        foreach (SchemaTypeInfo type in snapshot.Types.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
            sb.Append("- ").Append(type.Key).Append(" · ").Append(type.Name).Append(" · ").Append(type.Layout).Append('\n');

        sb.Append("Propriétés : ").Append(snapshot.Properties.Count).Append('\n');
        foreach (SchemaPropertyInfo prop in snapshot.Properties.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
            sb.Append("- ").Append(prop.Key).Append(" · ").Append(prop.Name).Append(" · ").Append(prop.Format).Append('\n');

        DeckleAnytypeSource.Log.GestureCompleted("schema_inspect_space", Elapsed(started));
        return sb.ToString().TrimEnd();
    }

    public async Task<string> PreviewAsync(string space, JsonObject manifestNode, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        string spaceId = aliases.Resolve(space);
        SchemaManifest manifest = SchemaManifest.Parse(manifestNode);
        SchemaSnapshot snapshot = await _snapshotReader.BuildAsync(spaceId, manifest, ct);
        SchemaPreview preview = SchemaPlanner.Build(space, spaceId, manifest, snapshot);

        _previewStore.Store(preview);

        DeckleAnytypeSource.Log.GestureCompleted("schema_preview", Elapsed(started));
        return SchemaPlanner.Render(preview);
    }

    public async Task<string> ApplyAsync(
        string space, string previewId, bool confirm, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        if (!confirm)
            throw new InvalidOperationException(
                "schema_apply exige confirm:true avec un preview_id relu juste avant.");

        if (!_previewStore.TryGet(previewId, out SchemaPreview preview))
            throw new InvalidOperationException(
                $"Preview inconnu « {previewId} ». Relance schema_preview.");

        if (!string.Equals(preview.SpaceAlias, space, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Le preview « {previewId} » cible l'espace {preview.SpaceAlias}, pas {space}.");

        if (preview.Conflicts.Count > 0)
            throw new InvalidOperationException(
                "Preview avec conflit : schema_apply refusé. Corrige le manifeste puis relance schema_preview.");

        using var _ = await api.AcquireWriteScopeAsync("schema_apply", preview.SpaceId, ct);

        SchemaSnapshot snapshot = await _snapshotReader.BuildAsync(preview.SpaceId, preview.Manifest, ct);
        SchemaPreview livePlan = SchemaPlanner.Build(preview.SpaceAlias, preview.SpaceId, preview.Manifest, snapshot);
        if (livePlan.Conflicts.Count > 0)
            throw new InvalidOperationException(
                "L'état Anytype a changé depuis le preview et produit maintenant un conflit. Relance schema_preview.");
        SchemaPlanner.EnsureNoUnpreviewedActions(preview, livePlan);

        var propertiesByKey = snapshot.Properties.ToDictionary(
            p => p.Key, p => p.Value, StringComparer.Ordinal);
        var typesByKey = snapshot.Types.ToDictionary(
            t => t.Key, t => t.Value, StringComparer.Ordinal);

        var applied = new List<string>();

        foreach (PropertySpec spec in preview.Manifest.Properties)
        {
            if (propertiesByKey.ContainsKey(spec.Key)) continue;

            JsonObject created = await api.CreatePropertyAsync(
                preview.SpaceId,
                new JsonObject
                {
                    ["key"] = spec.Key,
                    ["name"] = spec.Name,
                    ["format"] = spec.Format,
                },
                ct);

            JsonObject createdObject = SchemaApiJson.Payload(created);
            string id = SchemaApiJson.Id(createdObject);
            propertiesByKey[spec.Key] = new SchemaPropertyInfo(
                id,
                SchemaPlanner.OrDefault(SchemaApiJson.Str(createdObject, "key"), spec.Key),
                SchemaPlanner.OrDefault(SchemaApiJson.Str(createdObject, "name"), spec.Name),
                SchemaPlanner.OrDefault(SchemaApiJson.Str(createdObject, "format"), spec.Format));
            applied.Add($"propriété créée {spec.Key}");
        }

        foreach (PropertySpec spec in preview.Manifest.Properties)
        {
            if (!SchemaPlanner.IsTagFormat(spec.Format) || spec.Tags.Count == 0) continue;
            if (!propertiesByKey.TryGetValue(spec.Key, out SchemaPropertyInfo? property) || property.Id.Length == 0)
                throw new InvalidOperationException(
                    $"Impossible de créer les tags de « {spec.Key} » : id de propriété introuvable.");

            IReadOnlyDictionary<string, SchemaTagInfo> existingTags =
                snapshot.TagsByProperty.TryGetValue(spec.Key, out var tags)
                    ? tags
                    : new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);

            foreach (TagSpec tag in spec.Tags)
            {
                if (SchemaPlanner.HasTag(existingTags, tag)) continue;

                var payload = new JsonObject
                {
                    ["name"] = tag.Name,
                    ["color"] = tag.Color ?? "grey",
                };
                await api.CreatePropertyTagAsync(preview.SpaceId, property.Id, payload, ct);
                applied.Add($"tag créé {spec.Key}:{tag.Name}");
            }
        }

        foreach (TypeSpec spec in preview.Manifest.Types)
        {
            if (typesByKey.ContainsKey(spec.Key)) continue;

            IReadOnlyList<SchemaPropertyLinkInfo> links = SchemaPlanner.RequestedPropertyLinks(spec, propertiesByKey);

            JsonObject created = await api.CreateTypeAsync(
                preview.SpaceId,
                SchemaPlanner.TypeCreatePayload(spec, links),
                ct);

            JsonObject createdObject = SchemaApiJson.Payload(created);
            string id = SchemaApiJson.Id(createdObject);
            IReadOnlyList<SchemaPropertyLinkInfo> createdLinks = SchemaApiJson.PropertyLinks(createdObject);
            typesByKey[spec.Key] = new SchemaTypeInfo(
                id,
                spec.Key,
                SchemaPlanner.OrDefault(SchemaApiJson.Str(createdObject, "name"), spec.Name),
                SchemaPlanner.OrDefault(SchemaApiJson.Str(createdObject, "plural_name"), spec.PluralName),
                SchemaPlanner.OrDefault(SchemaApiJson.Str(createdObject, "layout"), spec.Layout),
                createdLinks.Count > 0 ? createdLinks : links);
            applied.Add($"type créé {spec.Key}");
        }

        foreach (TypeSpec type in preview.Manifest.Types)
        {
            if (type.Properties.Count == 0) continue;
            if (!typesByKey.TryGetValue(type.Key, out SchemaTypeInfo? liveType) || liveType.Id.Length == 0)
                throw new InvalidOperationException(
                    $"Impossible d'attacher les propriétés à « {type.Key} » : id de type introuvable.");

            var links = SchemaPlanner.ResolveTypePropertyLinks(liveType, propertiesByKey).ToList();

            bool changed = false;
            foreach (string propKey in type.Properties)
            {
                if (!propertiesByKey.TryGetValue(propKey, out SchemaPropertyInfo? property))
                    throw new InvalidOperationException(
                        $"Propriété « {propKey} » introuvable pour le type « {type.Key} ».");

                if (!links.Any(link => SchemaPlanner.LinkMatches(link, property)))
                {
                    links.Add(SchemaPlanner.LinkFrom(property));
                    changed = true;
                }
            }

            if (!changed) continue;

            await api.UpdateTypeAsync(
                preview.SpaceId,
                liveType.Id,
                new JsonObject
                {
                    ["name"] = SchemaPlanner.OrDefault(liveType.Name, type.Name),
                    ["plural_name"] = SchemaPlanner.OrDefault(liveType.PluralName, type.PluralName),
                    ["properties"] = SchemaPlanner.PropertyLinkArray(links),
                },
                ct);
            applied.Add($"propriétés attachées à {type.Key}");
        }

        _previewStore.Remove(previewId);

        DeckleAnytypeSource.Log.GestureCompleted("schema_apply", Elapsed(started));
        return applied.Count == 0
            ? $"Schéma inchangé : preview {previewId} ne contenait rien à appliquer."
            : "Schéma appliqué :\n" + string.Join("\n", applied.Select(a => "- " + a));
    }

    private static double Elapsed(DateTime startUtc) =>
        (DateTime.UtcNow - startUtc).TotalMilliseconds;
}
