using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Deckle.Anytype;

namespace Deckle.Home;

// Recreates a replay sheet on a target space through the typed gestures ONLY:
// every write is a HomeGestures create, update or complete, so the rehearsal
// proves the guichet itself can rebuild the space. The raw client only reads
// (types, templates, collections) to prepare the calls.
//
// Order (design note I7): zone, room, panel (roots first), circuit, point,
// system, device, component, utensil, plant, product, idea, worksite, todo.
// A relation is sent with the create when every object it names already
// exists on the target; otherwise it waits for one update pass at the end
// (controls between points, depends_on, about, a panel's upstream panel…).
// Every item carries force: a faithful copy is not a dictation, so twins
// the source holds are recreated as twins.
//
// A batch the gesture refuses is retried item by item, so one refused item
// (an option the target lacks, a category outside the 22) costs that item
// only. Refusals are counted per reason in memory; their messages, which
// name objects, go to the detail file beside the sheet, never elsewhere.
internal sealed partial class HomeSpaceReplayer
{
    private const int BatchSize = 100;
    private const int PageLimit = 100;

    internal static readonly IReadOnlyList<string> Order =
    [
        HomeSchema.Types.Zone, HomeSchema.Types.Room, HomeSchema.Types.Panel, HomeSchema.Types.Circuit,
        HomeSchema.Types.Point, HomeSchema.Types.System, HomeSchema.Types.Device, HomeSchema.Types.Component,
        HomeSchema.Types.Utensil, HomeSchema.Types.Plant, HomeSchema.Types.Product, HomeSchema.Types.Idea,
        HomeSchema.Types.Worksite, HomeSchema.Types.Todo,
    ];

    private readonly AnytypeApiClient _api;
    private readonly string _spaceId;
    private readonly HomeGestures _gestures;

    public HomeSpaceReplayer(AnytypeApiClient api, string targetSpaceId, IDeletionApproval? approval = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSpaceId);
        _api = api;
        _spaceId = targetSpaceId;
        _gestures = new HomeGestures(api, targetSpaceId, approval);
    }

    private sealed record Pending(HomeReplayObject Source, HomeCreateItem Item, Dictionary<string, List<string>> Deferred);

    public async Task<HomeReplayReport> ReplayAsync(
        HomeReplaySheet sheet, string? detailPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var report = new HomeReplayReport { SheetObjects = sheet.Objects.Count };
        var details = new HomeReplayDetails();
        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        var deferred = new List<(HomeReplayObject Source, Dictionary<string, List<string>> Relations)>();

        IReadOnlySet<string> collections = await TargetCollectionsAsync(ct).ConfigureAwait(false);
        IReadOnlyDictionary<string, string> templates = await SingleTemplatesAsync(ct).ConfigureAwait(false);

        foreach (string type in Order)
        {
            HomeReplayObject[] objects = OrderWithinType(
                type, sheet.Objects.Where(value => value.Type == type).ToArray());
            templates.TryGetValue(type, out string? template);
            foreach (HomeReplayObject[] chunk in objects.Chunk(BatchSize))
            {
                // Built at the batch, not before: a relation to an object of
                // an earlier batch is sent with the create.
                Pending[] batch = chunk.Select(value =>
                    Prepare(value, created, collections, template, report, details)).ToArray();
                await CreateBatchAsync(type, batch, created, report, details, ct).ConfigureAwait(false);
                foreach (Pending item in batch)
                    if (item.Deferred.Count > 0 && created.ContainsKey(item.Source.OldId))
                        deferred.Add((item.Source, item.Deferred));
                if (template is not null)
                    report.Templated += batch.Count(item => created.ContainsKey(item.Source.OldId));
            }
        }

        await UpdateRelationsAsync(deferred, created, report, details, ct).ConfigureAwait(false);
        await CompleteTodosAsync(sheet, created, report, details, ct).ConfigureAwait(false);

        report.CreatedIds = created;
        if (detailPath is not null) HomeReplaySheet.WriteJson(detailPath, details);
        return report;
    }

    private static Pending Prepare(
        HomeReplayObject source,
        IReadOnlyDictionary<string, string> created,
        IReadOnlySet<string> collections,
        string? template,
        HomeReplayReport report,
        HomeReplayDetails details)
    {
        var properties = new JsonObject();
        var deferred = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach ((string key, JsonNode? value) in source.Properties)
        {
            if (source.References.TryGetValue(key, out List<string>? references) && references.Count > 0)
            {
                if (references.All(created.ContainsKey))
                    properties[key] = new JsonArray(references.Select(id => (JsonNode?)created[id]).ToArray());
                else
                    deferred[key] = references;
                continue;
            }
            properties[key] = value?.DeepClone();
        }

        var kept = new List<string>();
        foreach (string collection in source.Collections)
        {
            if (collections.Contains(collection))
            {
                kept.Add(collection);
                continue;
            }
            report.CollectionsDropped++;
            details.DroppedCollections.Add(new(source.Type, source.OldId, source.Display, collection));
        }

        bool idea = source.Type == HomeSchema.Types.Idea;
        string? name = idea || source.Name.Length == 0 ? null : source.Name;
        var item = new HomeCreateItem(
            source.Code,
            name,
            properties.Count > 0 ? properties : null,
            kept.Count > 0 ? kept : null,
            Text: source.Body,
            Template: template,
            Force: true);
        return new Pending(source, item, deferred);
    }

    private async Task CreateBatchAsync(
        string type,
        IReadOnlyList<Pending> batch,
        Dictionary<string, string> created,
        HomeReplayReport report,
        HomeReplayDetails details,
        CancellationToken ct)
    {
        if (batch.Count == 0) return;
        try
        {
            string digest = await _gestures.CreateAsync(type, batch.Select(item => item.Item).ToArray(), ct)
                .ConfigureAwait(false);
            Record(type, batch, CreatedIds(digest), created, report);
            return;
        }
        catch (Exception ex) when (IsRefusal(ex) && batch.Count > 1)
        {
            // Nothing was written: a refusal comes from the preparation,
            // before the first POST. Retried one by one below.
        }
        catch (Exception ex) when (IsRefusal(ex))
        {
            Refuse("create", batch[0].Source, ex, report, details);
            return;
        }

        foreach (Pending item in batch)
        {
            try
            {
                string digest = await _gestures.CreateAsync(type, [item.Item], ct).ConfigureAwait(false);
                Record(type, [item], CreatedIds(digest), created, report);
            }
            catch (Exception ex) when (IsRefusal(ex))
            {
                Refuse("create", item.Source, ex, report, details);
            }
        }
    }

    private static void Record(
        string type,
        IReadOnlyList<Pending> batch,
        IReadOnlyList<string?> ids,
        Dictionary<string, string> created,
        HomeReplayReport report)
    {
        for (int index = 0; index < batch.Count; index++)
        {
            HomeReplaySheetReport.Increment(report.CreatedByType, type);
            string? id = index < ids.Count ? ids[index] : null;
            if (id is not null) created[batch[index].Source.OldId] = id;
            else report.CreatedWithoutId++;
        }
    }

    // The forward relations, one update per object, by type in the replay
    // order. A target that was never recreated (refused) is left out and
    // counted; the rest of the relation still lands.
    private async Task UpdateRelationsAsync(
        IReadOnlyList<(HomeReplayObject Source, Dictionary<string, List<string>> Relations)> deferred,
        IReadOnlyDictionary<string, string> created,
        HomeReplayReport report,
        HomeReplayDetails details,
        CancellationToken ct)
    {
        foreach (string type in Order)
        {
            var items = new List<(HomeReplayObject Source, HomeUpdateItem Item)>();
            foreach ((HomeReplayObject source, Dictionary<string, List<string>> relations) in
                     deferred.Where(entry => entry.Source.Type == type))
            {
                var properties = new JsonObject();
                foreach ((string key, List<string> references) in relations)
                {
                    var ids = new JsonArray();
                    foreach (string reference in references)
                    {
                        if (created.TryGetValue(reference, out string? id))
                        {
                            ids.Add(id);
                            continue;
                        }
                        report.RelationsDropped++;
                        details.DroppedRelations.Add(new(source.Type, source.OldId, source.Display, key, reference));
                    }
                    if (ids.Count > 0) properties[key] = ids;
                }
                if (properties.Count > 0)
                    items.Add((source, new HomeUpdateItem(created[source.OldId], null, properties)));
            }

            foreach (var chunk in items.Chunk(BatchSize))
            {
                try
                {
                    await _gestures.UpdateTypedAsync(type, chunk.Select(entry => entry.Item).ToArray(), ct)
                        .ConfigureAwait(false);
                    report.RelationUpdates += chunk.Length;
                    continue;
                }
                catch (Exception ex) when (IsRefusal(ex) && chunk.Length == 1)
                {
                    Refuse("update", chunk[0].Source, ex, report, details);
                    continue;
                }
                catch (Exception ex) when (IsRefusal(ex)) { }

                foreach ((HomeReplayObject source, HomeUpdateItem item) in chunk)
                {
                    try
                    {
                        await _gestures.UpdateTypedAsync(type, [item], ct).ConfigureAwait(false);
                        report.RelationUpdates++;
                    }
                    catch (Exception ex) when (IsRefusal(ex))
                    {
                        Refuse("update", source, ex, report, details);
                    }
                }
            }
        }
    }

    // A task done in the source is done on the target, through complete.
    private async Task CompleteTodosAsync(
        HomeReplaySheet sheet,
        IReadOnlyDictionary<string, string> created,
        HomeReplayReport report,
        HomeReplayDetails details,
        CancellationToken ct)
    {
        foreach (HomeReplayObject todo in sheet.Objects.Where(value =>
                     value.Type == HomeSchema.Types.Todo && value.Done == true))
        {
            if (!created.TryGetValue(todo.OldId, out string? id)) continue;
            try
            {
                await _gestures.CompleteAsync(id, ct).ConfigureAwait(false);
                report.CompletedTodos++;
            }
            catch (Exception ex) when (IsRefusal(ex))
            {
                Refuse("complete", todo, ex, report, details);
            }
        }
    }

    private static void Refuse(
        string stage, HomeReplayObject source, Exception ex, HomeReplayReport report, HomeReplayDetails details)
    {
        string reason = ReasonOf(ex);
        HomeReplaySheetReport.Increment(report.RefusedByReason, reason);
        details.Refusals.Add(new(stage, source.Type, source.OldId, source.Display, reason, ex.Message));
    }

    // A gesture's refusal: a validation that stopped before any write. A
    // schema the target does not hold, a transport failure or a partly
    // applied batch are not refusals of one item: they stop the replay.
    internal static bool IsRefusal(Exception ex) =>
        ex is ArgumentException or InvalidOperationException && ex is not HomeSchemaException;

    // A short, name-free reason for the counts, read from the gesture's
    // French message. The message itself goes to the detail file.
    internal static string ReasonOf(Exception ex)
    {
        string message = ex.Message;
        bool Has(string text) => message.Contains(text, StringComparison.Ordinal);
        if (Has("Option inconnue") || Has("Valeur inconnue")) return "option absente de la cible";
        if (Has("Catégorie inconnue")) return "catégorie hors des 22";
        if (Has("Code de point invalide") || Has("Code invalide") || Has("Code de pièce") && Has("deux lettres"))
            return "code invalide";
        if (Has("Code de pièce inconnu")) return "pièce absente";
        if (Has("existe déjà")) return "code déjà pris";
        if (Has("exige un code")) return "code absent";
        if (Has("exige un nom") || Has("Une idée est son texte")) return "nom ou texte absent";
        if (Has("Un composant n'existe que dans son système")) return "composant sans système";
        if (Has("Aucun objet Home") || Has("correspond à plusieurs objets")) return "relation introuvable";
        if (Has("Propriété inconnue") || Has("n'est pas autorisée") || Has("porte des fichiers"))
            return "propriété refusée";
        if (Has("Modèle")) return "modèle";
        if (Has("collection")) return "collection";
        return "autre refus";
    }

    // "Créé :" then one line per item, in the order of the items, ending in
    // "(id …)" when Anytype returned one.
    internal static IReadOnlyList<string?> CreatedIds(string digest) =>
        digest.Split('\n').Skip(1)
            .Select(line => CreatedIdPattern().Match(line.TrimEnd()) is { Success: true } match
                ? match.Groups["id"].Value
                : null)
            .ToArray();

    [GeneratedRegex(@"\(id (?<id>[^()\s]+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex CreatedIdPattern();

    // Panels root first, by the depth of their upstream chain within the
    // sheet; every other type keeps the sheet's order.
    internal static HomeReplayObject[] OrderWithinType(string type, HomeReplayObject[] objects)
    {
        if (type != HomeSchema.Types.Panel) return objects;
        Dictionary<string, HomeReplayObject> byId = objects.ToDictionary(value => value.OldId, StringComparer.Ordinal);
        int Depth(HomeReplayObject panel, HashSet<string> seen)
        {
            if (!seen.Add(panel.OldId)) return 0;
            return panel.References.TryGetValue(HomeSchema.Properties.UpstreamPanel, out List<string>? upstream)
                ? upstream.Where(byId.ContainsKey).Select(id => 1 + Depth(byId[id], seen)).DefaultIfEmpty(0).Max()
                : 0;
        }
        return objects.OrderBy(panel => Depth(panel, [])).ToArray();
    }

    // The names of the target's collections a membership may name: a name
    // two objects share would be ambiguous for the gesture, so it is not
    // offered and the membership is counted as dropped.
    private async Task<IReadOnlySet<string>> TargetCollectionsAsync(CancellationToken ct)
    {
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(_api, _spaceId, ct).ConfigureAwait(false);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, JsonObject> group in index.Objects
                     .GroupBy(value => HomeObjectJson.Name(value), StringComparer.OrdinalIgnoreCase))
            if (group.Key.Length > 0 && group.Count() == 1
                && HomeObjectJson.Layout(group.Single()) == "collection")
                names.Add(group.Key);
        return names;
    }

    // The template a type's objects are created from: its single template
    // when the target type has exactly one, else none.
    private async Task<IReadOnlyDictionary<string, string>> SingleTemplatesAsync(CancellationToken ct)
    {
        var typeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int offset = 0; ;)
        {
            JsonObject root = await _api.ListTypesAsync(_spaceId, offset, PageLimit, ct).ConfigureAwait(false);
            int returned = 0;
            foreach (JsonObject type in (root["data"] as JsonArray ?? []).OfType<JsonObject>())
            {
                returned++;
                string key = HomeObjectJson.String(type, "key");
                string id = HomeObjectJson.String(type, "id");
                if (key.Length > 0 && id.Length > 0) typeIds[key] = id;
            }
            if (root["pagination"]?["has_more"]?.GetValue<bool>() != true || returned == 0) break;
            offset += returned;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string type in Order)
        {
            if (!typeIds.TryGetValue(type, out string? typeId)) continue;
            JsonObject root = await _api.ListTemplatesAsync(_spaceId, typeId, 0, 2, ct).ConfigureAwait(false);
            JsonObject[] templates = (root["data"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
            bool more = root["pagination"]?["has_more"]?.GetValue<bool>() == true;
            if (templates.Length == 1 && !more
                && HomeObjectJson.String(templates[0], "name").Trim() is { Length: > 0 } name)
                result[type] = name;
        }
        return result;
    }
}

// Counts of a replay, in memory and safe to print: no name, no value.
internal sealed class HomeReplayReport
{
    public int SheetObjects { get; set; }
    public SortedDictionary<string, int> CreatedByType { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> RefusedByReason { get; } = new(StringComparer.Ordinal);
    public int RelationUpdates { get; set; }
    public int RelationsDropped { get; set; }
    public int CompletedTodos { get; set; }
    public int CollectionsDropped { get; set; }
    public int Templated { get; set; }
    public int CreatedWithoutId { get; set; }

    // Source id → target id, for the caller that follows the replay; ids
    // only, never names.
    public IReadOnlyDictionary<string, string> CreatedIds { get; set; } = new Dictionary<string, string>();

    public int Created => CreatedByType.Values.Sum();
    public int Refused => RefusedByReason.Values.Sum();
}

// The detail of a replay, written beside the sheet only: it names objects.
internal sealed class HomeReplayDetails
{
    [JsonPropertyName("refusals")]
    public List<HomeReplayRefusal> Refusals { get; } = [];

    [JsonPropertyName("dropped_relations")]
    public List<HomeReplayDroppedRelation> DroppedRelations { get; } = [];

    [JsonPropertyName("dropped_collections")]
    public List<HomeReplayDroppedCollection> DroppedCollections { get; } = [];
}

internal sealed record HomeReplayRefusal(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("old_id")] string OldId,
    [property: JsonPropertyName("display")] string Display,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("message")] string Message);

internal sealed record HomeReplayDroppedRelation(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("old_id")] string OldId,
    [property: JsonPropertyName("display")] string Display,
    [property: JsonPropertyName("property")] string Property,
    [property: JsonPropertyName("target_old_id")] string TargetOldId);

internal sealed record HomeReplayDroppedCollection(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("old_id")] string OldId,
    [property: JsonPropertyName("display")] string Display,
    [property: JsonPropertyName("collection")] string Collection);
