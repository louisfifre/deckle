using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Deckle.Anytype;

namespace Deckle.Home;

// Writes the remapped structure on the TARGET space, after the objects were
// replayed (the structure names the ids the replay created):
//
//   a. templates, one POST each, so the old template id maps to a new one;
//   b. type settings: default_view, default_template (mapped) and the
//      featured / hidden section of each property (the target type's whole
//      list is read first and sent back with only the sections changed, since
//      the patch replaces the list);
//   c. type views: every source view is inserted (each its own request, so one
//      refused view costs that view only), then the views the target held
//      before are deleted; the columns are then reconciled so the visible set
//      matches (a patch leaves unnamed columns as they are);
//   d. the section collections: found on the target by name, else created;
//      their items, mapped through every id map and added in source order;
//      then their views;
//   e. widgets in the source order, each target mapped (a type to the target
//      type, a collection to the target collection, another object through
//      the replay's id map) and each view mapped to the view written for it.
//
// Every refusal is caught and counted; its message, which names things, goes
// to the detail file beside the sheet. One failure never stops the rest.
internal sealed class HomeStructureWriter(AnytypeApiClient api, string spaceId)
{
    // add_items takes 1 to 1000 ids per op.
    private const int MaxItemsPerOp = 1000;

    private static readonly string[] LastOpenedKeys = ["lastOpenedDate", "last_opened_date"];

    private readonly HomeV2 _v2 = new(api, spaceId);
    private readonly HomeStructureWriteReport _report = new();
    private readonly List<HomeStructureRefusal> _refusals = [];

    public async Task<HomeStructureOutcome> WriteAsync(
        HomeStructure structure,
        IReadOnlyDictionary<string, string> createdObjectIds,
        string? detailPath,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(createdObjectIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);

        var ids = new Dictionary<string, string>(createdObjectIds, StringComparer.Ordinal);
        var remap = new HomeStructureRemap(ids);
        var plan = new HomeStructurePlan();

        await MapTypeIdsAsync(structure, ids, plan, ct).ConfigureAwait(false);
        await MapCollectionIdsAsync(structure, remap, ids, plan, ct).ConfigureAwait(false);
        await WriteTemplatesAsync(structure, remap, ids, plan, ct).ConfigureAwait(false);
        // The items name objects, templates, types and collections: every id
        // map is complete only now.
        await WriteCollectionItemsAsync(remap, ids, plan, ct).ConfigureAwait(false);

        foreach (HomeStructureType type in structure.Types)
        {
            HomeTypePlan typePlan = remap.TypePlan(type);
            plan.Types.Add(typePlan);
            await WriteTypeAsync(typePlan, remap, ids, plan, ct).ConfigureAwait(false);
        }

        foreach (HomeCollectionPlan collection in plan.Collections)
            await WriteCollectionAsync(collection, remap, plan, ct).ConfigureAwait(false);

        await WriteWidgetsAsync(structure, remap, plan, ct).ConfigureAwait(false);

        foreach ((string kind, int count) in remap.Dropped) _report.Dropped[kind] = count;
        if (detailPath is not null)
            HomeReplaySheet.WriteJson(detailPath, new HomeStructureDetails
            {
                Refusals = _refusals,
                Dropped = remap.Details,
            });
        return new HomeStructureOutcome(plan, _report);
    }

    // ── Ids of the target ───────────────────────────────────────────────

    // A source type's object id stands for the target type of the bridged key.
    private async Task MapTypeIdsAsync(
        HomeStructure structure, Dictionary<string, string> ids, HomeStructurePlan plan, CancellationToken ct)
    {
        try { plan.TypeIds = await _v2.TypeIdsAsync(ct).ConfigureAwait(false); }
        catch (HttpRequestException ex)
        {
            Refuse("type_list", "types", ex);
            return;
        }
        foreach (HomeStructureType type in structure.Types)
            if (type.TypeId.Length > 0 && plan.TypeIds.TryGetValue(HomeStructureRemap.TypeKey(type.Key), out string? id))
                ids[type.TypeId] = id;
    }

    // A section collection of the source is the target collection of the same
    // name when exactly one non-Home collection has it; otherwise it is created,
    // by name only. The section collections are not Home objects, so the object
    // replay never makes them. All of them exist before any item is mapped, a
    // collection may hold another.
    private async Task MapCollectionIdsAsync(
        HomeStructure structure, HomeStructureRemap remap, Dictionary<string, string> ids,
        HomeStructurePlan plan, CancellationToken ct)
    {
        if (structure.Collections.Count == 0) return;
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(api, spaceId, ct).ConfigureAwait(false);
        JsonObject[] collections = index.Objects.Where(value =>
            (HomeObjectJson.Layout(value) == "collection" || value["type"]?["layout"]?.GetValue<string>() == "collection")
            && !HomeSchema.CreatableTypes.Contains(HomeObjectJson.TypeKey(value), StringComparer.Ordinal)).ToArray();

        foreach (HomeStructureCollection source in structure.Collections)
        {
            string where = $"collection « {source.Name} »";
            JsonObject[] same = collections.Where(value =>
                string.Equals(HomeObjectJson.Name(value), source.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            string id;
            if (same.Length == 1)
            {
                id = HomeObjectJson.Id(same[0]);
                _report.Count(_report.Written, "collections_reused");
            }
            else
            {
                // 2026-11-09: POST collections takes a name and optional items
                // (every id must resolve in the space, else 400) and nothing
                // else, so no icon: the items follow by add_items once every id
                // can be mapped, and the icon is left out and counted.
                try
                {
                    id = IdOf(await _v2.PostAsync("collections", new JsonObject { ["name"] = source.Name }, null, ct)
                        .ConfigureAwait(false));
                    if (id.Length == 0) throw new InvalidOperationException("the answer carried no collection id");
                }
                catch (Exception ex) when (IsRefusal(ex))
                {
                    Refuse("collections", where, ex);
                    plan.MissingCollections.Add(source.Name);
                    continue;
                }
                _report.Count(_report.Written, "collections");
                if (source.Icon is not null) remap.Drop("collection_icon", where, "icon");
            }
            ids[source.OldId] = id;
            plan.Collections.Add(new HomeCollectionPlan(source, id));
        }
    }

    // The source items that map, in source order, added to the target
    // collection but for those it already holds. An item no map knows is
    // dropped and counted (collection_item).
    private async Task WriteCollectionItemsAsync(
        HomeStructureRemap remap, Dictionary<string, string> ids, HomeStructurePlan plan, CancellationToken ct)
    {
        foreach (HomeCollectionPlan collection in plan.Collections)
        {
            string where = $"collection « {collection.Source.Name} »";
            var mapped = new List<string>();
            foreach (string old in collection.Source.Items)
            {
                if (!ids.TryGetValue(old, out string? id)) remap.Drop("collection_item", where, old);
                else if (!mapped.Contains(id, StringComparer.Ordinal)) mapped.Add(id);
            }
            collection.Items = mapped;
            if (mapped.Count == 0) continue;

            var held = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (JsonObject row in await _v2.ListAsync($"collections/{collection.TargetId}/objects", ct).ConfigureAwait(false))
                    held.Add(HomeObjectJson.Id(row));
            }
            catch (HttpRequestException ex)
            {
                // The server ignores ids already present, so adding all is safe.
                Refuse("collection_items_read", where, ex);
            }

            string[] missing = mapped.Where(id => !held.Contains(id)).ToArray();
            for (int from = 0; from < missing.Length; from += MaxItemsPerOp)
            {
                string[] chunk = missing[from..Math.Min(from + MaxItemsPerOp, missing.Length)];
                // The op the v2 client names add_items: {"op":"add_items","items":[ids]}.
                var op = new JsonObject
                {
                    ["op"] = "add_items",
                    ["items"] = new JsonArray(chunk.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
                };
                bool ok = await TryAsync("collection_items", where, async () =>
                    await _v2.PatchAsync($"objects/{collection.TargetId}", new JsonObject { ["ops"] = new JsonArray(op) }, ct)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                if (ok) _report.Count(_report.Written, "collection_items", chunk.Length);
            }
        }
    }
    // ── Templates ───────────────────────────────────────────────────────

    private async Task WriteTemplatesAsync(
        HomeStructure structure, HomeStructureRemap remap, Dictionary<string, string> ids,
        HomeStructurePlan plan, CancellationToken ct)
    {
        foreach (HomeStructureTemplate template in structure.Templates)
        {
            HomeTemplatePlan? templatePlan = remap.TemplatePlan(template);
            if (templatePlan is null) continue;
            plan.Templates.Add(templatePlan);
            try
            {
                JsonNode? answer = await _v2.PostAsync(
                    "templates", templatePlan.Body, [new("create_missing_options", "true")], ct).ConfigureAwait(false);
                string id = IdOf(answer);
                if (id.Length == 0) throw new InvalidOperationException("the answer carried no template id");
                ids[template.OldId] = id;
                plan.TemplateIds[template.OldId] = id;
                _report.Count(_report.Written, "templates");
            }
            catch (Exception ex) when (IsRefusal(ex))
            {
                Refuse("templates", $"template « {template.Name} »", ex);
            }
        }
    }

    // ── Types ───────────────────────────────────────────────────────────

    private async Task WriteTypeAsync(
        HomeTypePlan type, HomeStructureRemap remap, Dictionary<string, string> ids,
        HomeStructurePlan plan, CancellationToken ct)
    {
        string where = $"type {type.Key}";
        JsonObject? document;
        try { document = await _v2.GetAsync($"types/{type.Key}", ct).ConfigureAwait(false) as JsonObject; }
        catch (HttpRequestException ex)
        {
            Refuse("types_read", where, ex);
            return;
        }
        if (document is null)
        {
            _report.Count(_report.Missing, "types");
            _refusals.Add(new("types_read", where, "the target serves no such type"));
            return;
        }

        // (b) Settings.
        var settings = new JsonObject();
        if (type.DefaultView is not null) settings["default_view"] = type.DefaultView;
        if (type.OldDefaultTemplate is not null)
        {
            if (ids.TryGetValue(type.OldDefaultTemplate, out string? template)) settings["default_template"] = template;
            else remap.Drop("id", where, type.OldDefaultTemplate);
        }
        if (settings.Count > 0)
            await TryAsync("type_settings", where, async () =>
                await _v2.PatchAsync($"types/{type.Key}", settings, ct).ConfigureAwait(false)).ConfigureAwait(false);

        await WriteSectionsAsync(type, document, remap, where, ct).ConfigureAwait(false);

        // (c) Views.
        JsonArray targetViews = HomeV2.DataviewBlock(document)?["views"] as JsonArray ?? [];
        await WriteViewsAsync(
            "type_views", where, type.OldTypeId, type.Views, targetViews,
            ops => _v2.PatchAsync($"types/{type.Key}", new JsonObject { ["ops"] = ops }, ct),
            async () => HomeV2.DataviewBlock(
                (await _v2.GetAsync($"types/{type.Key}", ct).ConfigureAwait(false) as JsonObject) ?? [])
                ?["views"] as JsonArray ?? [],
            plan, ct).ConfigureAwait(false);
    }

    // The type's whole property list goes back with only the sections of the
    // plan changed; a property the plan does not name keeps its section.
    private async Task WriteSectionsAsync(
        HomeTypePlan type, JsonObject document, HomeStructureRemap remap, string where, CancellationToken ct)
    {
        JsonArray current = (document["type_settings"] as JsonObject)?["property_definitions"] as JsonArray ?? [];
        string[] attached = current.OfType<JsonObject>().Select(entry => HomeObjectJson.String(entry, "property")).ToArray();

        foreach (HomeSectionPlan section in type.Properties.Where(section => section.Section is not null))
            if (!attached.Contains(section.Property, StringComparer.Ordinal))
                remap.Drop("section", where, section.Property);
        if (!type.Properties.Any(section => attached.Contains(section.Property, StringComparer.Ordinal))) return;

        var definitions = new JsonArray();
        foreach (JsonObject entry in current.OfType<JsonObject>())
        {
            string property = HomeObjectJson.String(entry, "property");
            if (property.Length == 0) continue;
            HomeSectionPlan? planned = type.Properties.FirstOrDefault(section => section.Property == property);
            string? section = planned is not null ? planned.Section : HomeObjectJson.String(entry, "section") is { Length: > 0 } kept ? kept : null;
            var definition = new JsonObject { ["property"] = property };
            if (section is not null) definition["section"] = section;
            definitions.Add(definition);
        }
        await TryAsync("type_sections", where, async () => await _v2.PatchAsync(
            $"types/{type.Key}", new JsonObject { ["property_definitions"] = definitions }, ct).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    // ── Collections ─────────────────────────────────────────────────────

    private async Task WriteCollectionAsync(
        HomeCollectionPlan collection, HomeStructureRemap remap, HomeStructurePlan plan, CancellationToken ct)
    {
        string where = $"collection « {collection.Source.Name} »";
        // The collection's views have no type to hold a property to: any
        // manifest property may be a column.
        collection.Views = remap.CollectionViewPlans(collection.Source);

        JsonArray targetViews;
        try { targetViews = new JsonArray(((await _v2.ListAsync($"collections/{collection.TargetId}/views", ct).ConfigureAwait(false)))
            .Select(view => (JsonNode?)view).ToArray()); }
        catch (HttpRequestException ex)
        {
            Refuse("collection_views", where, ex);
            return;
        }

        await WriteViewsAsync(
            "collection_views", where, collection.Source.OldId, collection.Views, targetViews,
            ops => _v2.PatchAsync($"objects/{collection.TargetId}", new JsonObject { ["ops"] = ops }, ct),
            async () => new JsonArray((await _v2.ListAsync($"collections/{collection.TargetId}/views", ct).ConfigureAwait(false))
                .Select(view => (JsonNode?)view).ToArray()),
            plan, ct).ConfigureAwait(false);
    }

    // ── Views (types and collections) ───────────────────────────────────

    private async Task WriteViewsAsync(
        string stage,
        string where,
        string owner,
        IReadOnlyList<HomeViewPlan> plans,
        JsonArray existing,
        Func<JsonArray, Task<JsonNode?>> patch,
        Func<Task<JsonArray>> readViews,
        HomeStructurePlan plan,
        CancellationToken ct)
    {
        // The views the target held before our inserts (its default view).
        string[] before = existing.OfType<JsonObject>().Select(view => HomeObjectJson.Id(view))
            .Where(id => id.Length > 0).ToArray();
        var written = new List<(HomeViewPlan View, string TargetId)>();

        // 2026-11-09: every source view is INSERTED, never written over a view
        // that already existed. On the real server an update_view with `set.sorts`
        // on an existing view answers right and reads right, then a deferred step
        // seconds later cuts the type's default view back to its first sort or
        // appends the old sorts again after the new ones. A view made by
        // insert_view keeps its sorts (stable over 30 s), and later columns-only
        // update_view ops on it do not disturb them.
        foreach (HomeViewPlan view in plans)
        {
            JsonObject op = new()
            {
                ["op"] = "insert_view",
                ["name"] = view.Name.Length > 0 ? view.Name : "Vue",
                ["set"] = view.Set.DeepClone(),
                ["columns"] = WithoutLastOpened(view.Columns),
            };

            JsonNode? answer = null;
            bool ok = await TryAsync(stage, $"{where}, view « {view.Name} »", async () =>
                answer = await patch(new JsonArray(op)).ConfigureAwait(false)).ConfigureAwait(false);
            if (!ok) continue;

            string? targetId = CreatedViewId(answer);
            if (targetId is null)
            {
                Refuse(stage, $"{where}, view « {view.Name} »", new InvalidOperationException("the answer carried no view id"));
                continue;
            }
            written.Add((view, targetId));
            plan.ViewIds[HomeStructurePlan.ViewKey(owner, view.OldId)] = targetId;
            _report.Count(_report.Written, stage);
        }
        if (written.Count == 0) return;

        // 2026-11-09: the views that stood before are deleted once a replacement
        // exists (delete_view works, including the type's view `default`; the
        // type is left with only the inserted views, stable). One op per request,
        // so one refusal costs one view and is counted.
        foreach (string id in before)
        {
            JsonObject delete = new() { ["op"] = "delete_view", ["view"] = id };
            await TryAsync("view_delete", $"{where}, view {id}", async () =>
                await patch(new JsonArray(delete)).ConfigureAwait(false)).ConfigureAwait(false);
        }

        // Columns the insert did not name keep their state: hide, one view at
        // a time, those the target shows and the source does not.
        JsonArray now;
        try { now = await readViews().ConfigureAwait(false); }
        catch (HttpRequestException ex)
        {
            Refuse("column_reconcile", where, ex);
            return;
        }
        foreach ((HomeViewPlan view, string targetId) in written)
        {
            JsonObject? found = now.OfType<JsonObject>().FirstOrDefault(value => HomeObjectJson.Id(value) == targetId);
            if (found is null) continue;
            var hide = new JsonObject();
            bool lastOpenedShown = false;
            foreach (JsonObject column in (found["columns"] as JsonArray ?? []).OfType<JsonObject>())
            {
                string property = HomeObjectJson.String(column, "property");
                bool hidden = column["hidden"] is JsonValue flag && flag.TryGetValue(out bool value) && value;
                // 2026-11-09: lastOpenedDate is never sent. Hiding it (as
                // `lastOpenedDate` or `last_opened_date`) answers 200 but
                // APPENDS a second column entry with the same key, and null does
                // nothing, so the API cannot hide or remove it. It stays visible
                // on an inserted view (the person hides it by hand); counted.
                if (LastOpenedKeys.Contains(property))
                {
                    lastOpenedShown |= !hidden;
                    continue;
                }
                if (property.Length > 0 && !hidden && !view.Columns.ContainsKey(property))
                    hide[property] = new JsonObject { ["hidden"] = true };
            }
            if (lastOpenedShown) _report.Count(_report.Dropped, "unhideable_column");
            if (hide.Count == 0) continue;
            JsonObject op = new() { ["op"] = "update_view", ["view"] = targetId, ["columns"] = hide };
            await TryAsync("column_reconcile", $"{where}, view « {view.Name} »", async () =>
                await patch(new JsonArray(op)).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
    // The column patch minus lastOpenedDate: see the reconcile below.
    private static JsonObject WithoutLastOpened(JsonObject columns)
    {
        var kept = (JsonObject)columns.DeepClone();
        foreach (string key in LastOpenedKeys) kept.Remove(key);
        return kept;
    }

    private static string? CreatedViewId(JsonNode? answer) =>
        (answer as JsonObject)?["created_views"] is JsonObject created
        && created.Select(pair => pair.Value).FirstOrDefault() is JsonValue id
        && id.TryGetValue(out string? text) && !string.IsNullOrEmpty(text)
            ? text
            : null;

    // ── Widgets ─────────────────────────────────────────────────────────

    private async Task WriteWidgetsAsync(
        HomeStructure structure, HomeStructureRemap remap, HomeStructurePlan plan, CancellationToken ct)
    {
        foreach (HomeStructureWidget source in structure.Widgets)
        {
            HomeWidgetPlan? widget = remap.WidgetPlan(source);
            if (widget is null) continue;

            string? viewId = null;
            if (widget.OldViewId is not null)
            {
                if (plan.ViewIds.TryGetValue(HomeStructurePlan.ViewKey(source.Target, widget.OldViewId), out string? mapped)) viewId = mapped;
                else remap.Drop("widget_view", $"widget {widget.OldId}", widget.OldViewId);
            }
            widget = widget with { ViewId = viewId };
            plan.Widgets.Add(widget);

            var body = new JsonObject { ["target"] = widget.Target, ["scope"] = "space", ["position"] = "last" };
            if (widget.Layout is not null) body["layout"] = widget.Layout;
            // A link widget takes no limit.
            if (widget.Limit is int limit && widget.Layout != "link") body["limit"] = limit;
            if (viewId is not null) body["view_id"] = viewId;

            bool ok = await TryAsync("widgets", $"widget {widget.OldId}", async () =>
                await _v2.PostAsync("widgets", body, null, ct).ConfigureAwait(false)).ConfigureAwait(false);
            if (ok) _report.Count(_report.Written, "widgets");
            else plan.Widgets.Remove(widget);
        }
    }

    // ── Shared ──────────────────────────────────────────────────────────

    private async Task<bool> TryAsync(string stage, string what, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsRefusal(ex))
        {
            Refuse(stage, what, ex);
            return false;
        }
    }

    // A refusal of the API, or an answer that cannot be read: it costs the
    // item. Cancellation and programming errors are not refusals.
    private static bool IsRefusal(Exception ex) => ex is HttpRequestException or InvalidOperationException;

    private void Refuse(string stage, string what, Exception ex)
    {
        _report.Count(_report.Refused, stage);
        _refusals.Add(new(stage, what, ex.Message));
    }

    private static string IdOf(JsonNode? answer) =>
        answer is JsonObject value
            ? HomeObjectJson.Id(value) is { Length: > 0 } id ? id : HomeObjectJson.Id(HomeObjectJson.Unwrap(value))
            : "";
}

// What the write settled on, for the read-back to hold the target against.
internal sealed class HomeStructurePlan
{
    public List<HomeTypePlan> Types { get; } = [];
    public List<HomeCollectionPlan> Collections { get; } = [];

    // Section collections the target neither had nor could be given.
    public List<string> MissingCollections { get; } = [];
    public List<HomeTemplatePlan> Templates { get; } = [];
    public List<HomeWidgetPlan> Widgets { get; } = [];

    // (Source owner id, source view id) → the id of the view written for it. A
    // view id is only unique inside its owner: every type's first view is
    // called `default`.
    public static string ViewKey(string owner, string oldViewId) => owner + "|" + oldViewId;

    public Dictionary<string, string> ViewIds { get; } = new(StringComparer.Ordinal);

    // Source template id → the id of the template created for it.
    public Dictionary<string, string> TemplateIds { get; } = new(StringComparer.Ordinal);

    // The target's type key → object id.
    public Dictionary<string, string> TypeIds { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class HomeCollectionPlan(HomeStructureCollection source, string targetId)
{
    public HomeStructureCollection Source { get; } = source;
    public string TargetId { get; } = targetId;
    public List<HomeViewPlan> Views { get; set; } = [];

    // The target ids of the source items that mapped, in source order.
    public List<string> Items { get; set; } = [];
}

internal sealed record HomeStructureOutcome(HomeStructurePlan Plan, HomeStructureWriteReport Report);

// Counts only, safe to print.
internal sealed class HomeStructureWriteReport
{
    public SortedDictionary<string, int> Written { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Refused { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Missing { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Dropped { get; } = new(StringComparer.Ordinal);

    public int RefusedTotal => Refused.Values.Sum();

    internal void Count(SortedDictionary<string, int> counts, string key, int by = 1) =>
        counts[key] = counts.TryGetValue(key, out int value) ? value + by : by;
}

internal sealed class HomeStructureDetails
{
    [JsonPropertyName("refusals")]
    public List<HomeStructureRefusal> Refusals { get; set; } = [];

    [JsonPropertyName("dropped")]
    public List<HomeStructureDrop> Dropped { get; set; } = [];
}

internal sealed record HomeStructureRefusal(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("what")] string What,
    [property: JsonPropertyName("message")] string Message);
