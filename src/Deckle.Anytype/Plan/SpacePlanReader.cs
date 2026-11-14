using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// The ids of a live space, behind the symbols a plan uses. Filled by the reader,
// then kept up to date by the builder as it creates things.
internal sealed class LivePlanRefs : IPlanRefs
{
    private readonly Dictionary<string, string> _typeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _typeKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _objectIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _objectNames = new(StringComparer.Ordinal);

    public void SetType(string key, string id) { _typeIds[key] = id; _typeKeys[id] = key; }

    public void SetObject(string name, string id) { _objectIds[name] = id; _objectNames[id] = name; }

    public bool HasType(string key) => _typeIds.ContainsKey(key);

    public string TypeId(string key) =>
        _typeIds.TryGetValue(key, out string? id) ? id : throw new InvalidOperationException($"Type « {key} » inconnu de l'espace.");

    public string? TypeKey(string id) => _typeKeys.GetValueOrDefault(id);

    public string ObjectId(string name) =>
        _objectIds.TryGetValue(name, out string? id) ? id : throw new InvalidOperationException($"Objet « {name} » inconnu de l'espace.");

    public string? ObjectName(string id) => _objectNames.GetValueOrDefault(id);
}

// One template as the space holds it.
internal sealed record LiveTemplate(string Id, string Name, string For, bool IsDefault);

// What the reader returns: the plan the space amounts to, plus the ids the
// builder needs to write to it.
internal sealed record SpacePlanRead(
    JsonObject Plan,
    LivePlanRefs Refs,
    IReadOnlyDictionary<string, string> TypeIds,
    IReadOnlyList<LiveTemplate> Templates,
    IReadOnlyDictionary<string, string> PropertyIds);

// Reads a live space into the plan that would rebuild it: the reverse of the
// builder. The plan it returns is in the form a person writes, so a space can be
// read once, corrected by hand and built again elsewhere.
//
// What it leaves out on purpose: the types the app owns outright (page, note,
// collection, query, files, participants...), which a plan cannot change, and
// every object, which a plan only carries when asked. A page is read only when
// its name is given, because a space holds thousands and a plan holds a few.
internal sealed class SpacePlanReader(AnytypeApiClient api)
{
    // Types the app keeps for itself. A plan can neither create nor reshape them.
    internal static readonly IReadOnlySet<string> SystemTypeKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "audio", "bookmark", "chat_derived", "collection", "dashboard", "date", "discussion", "file",
        "image", "note", "object_type", "page", "participant", "query", "set", "template", "video",
        "relation", "relation_option", "space", "space_view", "chat", "widget", "profile",
    };

    // The properties every type pins in its header or hides, whatever the plan
    // says: reading them back would only add noise.
    private static readonly HashSet<string> SystemHeader = new(StringComparer.Ordinal) { "type", "tag", "backlinks" };
    private static readonly HashSet<string> SystemHidden = new(StringComparer.Ordinal)
    {
        "last_modified_date", "last_modified_by", "lastOpenedDate", "last_opened_date",
        "backlinks", "created_date", "creator", "links",
    };

    // v2 words for a layout, as the manifest says them.
    internal static string LayoutToPlan(string v2Layout) => v2Layout switch { "todo" => "action", _ => v2Layout };

    public async Task<SpacePlanRead> ReadAsync(string spaceId, IReadOnlyCollection<string> pageNames, CancellationToken ct)
    {
        var v2 = new PlanApi(api.V2, spaceId);
        var refs = new LivePlanRefs();

        // Properties and options, through v1: only it gives an option's id, and a
        // filter holds option ids.
        var snapshotReader = new SchemaSnapshotReader(api);
        SchemaSnapshot first = await snapshotReader.ReadAsync(spaceId, null, ct).ConfigureAwait(false);
        string[] tagKeys = first.Properties.Values
            .Where(p => SchemaPlanner.IsTagFormat(p.Format)).Select(p => p.Key).ToArray();
        SchemaSnapshot snapshot = await snapshotReader.ReadAsync(spaceId, tagKeys, ct).ConfigureAwait(false);

        var notes = new JsonArray();
        var properties = new JsonArray();
        foreach (SchemaPropertyInfo prop in snapshot.Properties.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (KnownProperties.IsSystem(prop.Key)) continue;
            var node = new JsonObject { ["key"] = prop.Key, ["name"] = prop.Name, ["format"] = prop.Format };
            if (snapshot.TagsByProperty.TryGetValue(prop.Key, out var tags) && tags.Count > 0)
            {
                var list = new JsonArray();
                // The snapshot indexes an option under more than one key, so its values
                // list it more than once: one option is one id.
                foreach (var byName in tags.Values.DistinctBy(t => t.Id).GroupBy(t => t.Name, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    if (byName.Count() > 1)
                        notes.Add($"propriété {prop.Key} : l'option « {byName.Key} » existe {byName.Count()} fois ; une seule est lue");
                    list.Add(new JsonObject { ["name"] = byName.Key, ["color"] = byName.First().Color });
                }
                node["tags"] = list;
            }
            properties.Add(node);
        }

        // Types.
        var typeRows = await v2.ListAsync("types", null, ct).ConfigureAwait(false);
        var typeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var typeDocs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (JsonObject row in typeRows)
        {
            string key = SchemaApiJson.Str(row, "key");
            if (key.Length == 0) continue;
            JsonObject doc = await v2.GetAsync($"types/{Uri.EscapeDataString(key)}", ct).ConfigureAwait(false);
            string id = SchemaApiJson.Id(doc);
            if (id.Length == 0) continue;
            typeIds[key] = id;
            typeDocs[key] = doc;
            refs.SetType(key, id);
        }

        // Templates, with bodies. Their blocks name types, so the types come first.
        var liveTemplates = new List<LiveTemplate>();
        var templates = new JsonArray();
        foreach (JsonObject row in await v2.ListAsync("templates", null, ct).ConfigureAwait(false))
        {
            string id = SchemaApiJson.Id(row);
            string name = SchemaApiJson.Str(row, "name");
            string forKey = SchemaApiJson.Str(row, "template_for");
            if (id.Length == 0 || name.Length == 0 || forKey.Length == 0) continue;
            liveTemplates.Add(new LiveTemplate(id, name, forKey, row["default"]?.GetValue<bool>() == true));
            if (SystemTypeKeys.Contains(forKey)) continue;
            if (templates.OfType<JsonObject>().Any(t => Str0(t, "name") == name && Str0(t, "for") == forKey))
            {
                notes.Add($"modèle « {name} » ({forKey}) existe plusieurs fois ; le premier est lu");
                continue;
            }
            JsonObject doc = await v2.GetAsync($"objects/{id}", ct).ConfigureAwait(false);
            var template = new JsonObject { ["name"] = name, ["for"] = forKey };
            template["blocks"] = DropEmptyViews(PlanViews.BodyFromWire(doc["blocks"] as JsonArray, refs), $"modèle « {name} » ({forKey})", notes);
            templates.Add(template);
        }

        var types = new JsonArray();
        foreach ((string key, JsonObject doc) in typeDocs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (SystemTypeKeys.Contains(key)) continue;
            types.Add(TypeToPlan(key, doc, snapshot, liveTemplates, refs));
        }

        // Collections and queries, found by their type.
        var collections = new JsonArray();
        var queries = new JsonArray();
        var searcher = new AnytypeV2Searcher(api.V2, spaceId);
        foreach (string kind in new[] { "collection", "query" })
        {
            IReadOnlyList<AnytypeV2SearchRow> rows = await searcher
                .SearchAllAsync(new AnytypeV2SearchRequest { Type = kind }, ct).ConfigureAwait(false);
            foreach (AnytypeV2SearchRow row in rows.Where(r => r.Type == kind).OrderBy(r => r.Name, StringComparer.Ordinal))
            {
                refs.SetObject(row.Name, row.Id);
                JsonObject doc = await v2.GetAsync($"objects/{row.Id}", ct).ConfigureAwait(false);
                var item = new JsonObject { ["name"] = row.Name };
                if (kind == "query")
                {
                    var source = new JsonObject();
                    if (doc["query_source"]?["types"] is JsonArray st) source["types"] = TypeKeys(st, refs);
                    if (doc["query_source"]?["properties"] is JsonArray sp) source["properties"] = sp.DeepClone();
                    item["source"] = source;
                }
                else
                {
                    var items = new JsonArray();
                    foreach (JsonNode? member in doc["collection_items"] as JsonArray ?? [])
                    {
                        string memberId = member?.GetValue<string>() ?? "";
                        if (refs.TypeKey(memberId) is { } tk) items.Add("type:" + tk);
                        else if (refs.ObjectName(memberId) is { } on) items.Add("object:" + on);
                        // A plan holds no opaque id (ADR-0003): a member that is neither a type nor a named object is left out.
                        else notes.Add($"collection « {row.Name} » : un membre qui n'est ni un type ni un objet connu est ignoré");
                    }
                    item["items"] = items;
                }
                item["views"] = ViewsOf(doc, refs);
                (kind == "query" ? queries : collections).Add(item);
            }
        }

        // Pages, only those asked for.
        var pages = new JsonArray();
        foreach (string pageName in pageNames)
        {
            IReadOnlyList<AnytypeV2SearchRow> hits = await new AnytypeV2Searcher(api.V2, spaceId)
                .SearchAllAsync(new AnytypeV2SearchRequest { Type = "page", Query = pageName }, ct).ConfigureAwait(false);
            AnytypeV2SearchRow? hit = hits.FirstOrDefault(r => r.Name == pageName);
            if (hit is null) continue;
            refs.SetObject(hit.Name, hit.Id);
            JsonObject doc = await v2.GetAsync($"objects/{hit.Id}", ct).ConfigureAwait(false);
            pages.Add(new JsonObject
            {
                ["name"] = pageName,
                ["blocks"] = DropEmptyViews(PlanViews.BodyFromWire(doc["blocks"] as JsonArray, refs), $"page « {pageName} »", notes),
            });
        }

        // The sidebar.
        var widgets = new JsonArray();
        foreach (JsonObject row in await v2.ListAsync("widgets", [new("scope", "space")], ct).ConfigureAwait(false))
        {
            string target = SchemaApiJson.Str(row, "target");
            string symbol = refs.TypeKey(target) is { } tk ? "type:" + tk : refs.ObjectName(target) is { } on ? "object:" + on : target;
            var widget = new JsonObject { ["target"] = symbol };
            foreach (string field in new[] { "layout", "limit" })
                if (row[field] is { } value) widget[field] = value.DeepClone();
            // A plan names a view by its name; the sidebar stores its id.
            if (SchemaApiJson.Str(row, "view_id") is { Length: > 0 } viewId
                && await ViewNameAsync(v2, target, viewId, typeDocs, refs, ct).ConfigureAwait(false) is { } viewName)
                widget["view"] = viewName;
            widgets.Add(widget);
        }

        var plan = new JsonObject { ["plan_version"] = SpacePlan.CurrentVersion };
        if (notes.Count > 0) plan["reader_notes"] = notes;
        plan["properties"] = properties;
        plan["types"] = types;
        plan["templates"] = templates;
        plan["collections"] = collections;
        plan["queries"] = queries;
        if (pages.Count > 0) plan["pages"] = pages;
        if (widgets.Count > 0) plan["widgets"] = widgets;

        return new SpacePlanRead(
            plan, refs, typeIds, liveTemplates,
            snapshot.Properties.ToDictionary(p => p.Key, p => p.Value.Id, StringComparer.Ordinal));
    }

    // A view block with no source shows nothing and names nothing: a shell the
    // app left behind. A plan cannot state it, so it is left out and said.
    private static JsonArray DropEmptyViews(JsonArray body, string label, JsonArray notes)
    {
        var kept = new JsonArray();
        foreach (JsonNode? block in body)
        {
            if (block is JsonObject obj && Str0(obj, "type") == "dataview" && Str0(obj, "source").Length == 0)
            {
                notes.Add($"{label} : un bloc de vues sans source est ignoré ({(obj["views"] as JsonArray)?.Count ?? 0} vue(s) vide(s))");
                continue;
            }
            kept.Add(block?.DeepClone());
        }
        return PruneEmptyContainers(kept);
    }

    // The server drops a row with no column and a column with nothing in it when
    // a body is written, so a plan that keeps them never reads back equal.
    private static JsonArray PruneEmptyContainers(JsonArray body)
    {
        var blocks = body.OfType<JsonObject>().ToList();
        bool removed;
        do
        {
            removed = false;
            for (int i = 0; i < blocks.Count; i++)
            {
                string type = Str0(blocks[i], "type");
                if (type is not ("row" or "column")) continue;
                int indent = blocks[i]["indent"]?.GetValue<int>() ?? 0;
                bool hasChild = i + 1 < blocks.Count && (blocks[i + 1]["indent"]?.GetValue<int>() ?? 0) > indent;
                if (hasChild) continue;
                blocks.RemoveAt(i);
                removed = true;
                break;
            }
        } while (removed);
        return new JsonArray(blocks.Select(b => (JsonNode?)b.DeepClone()).ToArray());
    }

    private static string Str0(JsonObject node, string key) => node[key]?.GetValue<string>() ?? "";

    private static async Task<string?> ViewNameAsync(
        PlanApi v2, string targetId, string viewId, Dictionary<string, JsonObject> typeDocs, LivePlanRefs refs, CancellationToken ct)
    {
        JsonObject? doc = refs.TypeKey(targetId) is { } typeKey && typeDocs.TryGetValue(typeKey, out JsonObject? typeDoc)
            ? typeDoc
            : await v2.TryGetAsync($"objects/{targetId}", ct).ConfigureAwait(false);
        JsonObject? dataview = (doc?["blocks"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(b => Str0(b, "type") == "dataview");
        JsonObject? view = (dataview?["views"] as JsonArray ?? []).OfType<JsonObject>()
            // The API shows a view by a short id and the sidebar keeps the long one: the
            // short id is the end of the long one.
            .FirstOrDefault(v => Str0(v, "id") is { Length: > 0 } id && (id == viewId || viewId.EndsWith(id, StringComparison.Ordinal)));
        return view is null ? null : Str0(view, "name");
    }

    private static JsonArray TypeKeys(JsonArray ids, LivePlanRefs refs)
    {
        var result = new JsonArray();
        foreach (JsonNode? id in ids)
        {
            string text = id?.GetValue<string>() ?? "";
            result.Add(refs.TypeKey(text) ?? (text.StartsWith("type-", StringComparison.Ordinal) ? text[5..] : text));
        }
        return result;
    }

    private static JsonArray ViewsOf(JsonObject doc, LivePlanRefs refs)
    {
        var dataview = (doc["blocks"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(b => b["type"]?.GetValue<string>() == "dataview");
        var views = new JsonArray();
        foreach (JsonObject view in (dataview?["views"] as JsonArray ?? []).OfType<JsonObject>())
            views.Add(PlanViews.FromWire(view, refs));
        return views;
    }

    private static JsonObject TypeToPlan(
        string key, JsonObject doc, SchemaSnapshot snapshot, IReadOnlyList<LiveTemplate> templates, LivePlanRefs refs)
    {
        JsonObject settings = doc["type_settings"] as JsonObject ?? [];
        var type = new JsonObject
        {
            ["key"] = key,
            ["name"] = doc["properties"]?["name"]?.DeepClone(),
            ["plural_name"] = settings["plural_name"]?.DeepClone(),
            ["layout"] = LayoutToPlan(settings["layout"]?.GetValue<string>() ?? "basic"),
        };
        if (doc["properties"]?["description"]?.GetValue<string>() is { Length: > 0 } description)
            type["description"] = description;
        if (doc["icon"] is JsonObject icon) type["icon"] = icon.DeepClone();

        // Membership and order come from the v1 link list, which is what the
        // schema stage compares against; the v2 definitions only say which
        // properties sit in the header or in the hidden section.
        var regular = new JsonArray();
        if (snapshot.Types.TryGetValue(key, out SchemaTypeInfo? live))
            foreach (SchemaPropertyLinkInfo link in live.PropertyLinks)
            {
                string linkKey = link.Key.Length > 0
                    ? link.Key
                    : snapshot.Properties.Values.FirstOrDefault(p => p.Id == link.Id)?.Key ?? "";
                if (linkKey.Length > 0 && !SystemHeader.Contains(linkKey)) regular.Add(linkKey);
            }
        var featured = new JsonArray();
        var hidden = new JsonArray();
        foreach (JsonObject def in (settings["property_definitions"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string property = SchemaApiJson.Str(def, "property");
            if (SystemHeader.Contains(property) || SystemHidden.Contains(property)) continue;
            string section = SchemaApiJson.Str(def, "section");
            if (section == "featured") featured.Add(property);
            else if (section == "hidden") hidden.Add(property);
        }
        // A hidden property is not in the v1 link list; the plan still counts it as the type's own.
        foreach (JsonNode? hiddenKey in hidden) regular.Add(hiddenKey?.DeepClone());
        type["properties"] = regular;
        if (featured.Count > 0 || hidden.Count > 0)
            type["panel"] = new JsonObject { ["featured"] = featured, ["hidden"] = hidden };
        string defaultId = settings["default_template"]?.GetValue<string>() ?? "";
        LiveTemplate? defaultTemplate = templates.FirstOrDefault(t => t.Id == defaultId)
            ?? templates.FirstOrDefault(t => t.For == key && t.IsDefault);
        if (defaultTemplate is not null && defaultTemplate.Name.Length > 0) type["default_template"] = defaultTemplate.Name;

        type["views"] = ViewsOf(doc, refs);
        return type;
    }
}

// The properties a fresh space ships, by key (measured 2026-11-14 on a space the
// bot created). A plan does not declare them; it only points at them.
internal static class KnownProperties
{
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal)
    {
        "name", "added_date", "audio_album", "artist", "assignee", "audio_genre", "backlinks", "aperture",
        "exposure", "camera", "creator", "created_in_context", "created_date", "description", "done",
        "due_date", "file_ext", "focal_ratio", "height_in_pixels", "import_type", "camera_iso",
        "last_message_date", "last_modified_by", "last_modified_date", "last_opened_date", "lastOpenedDate",
        "linked_projects", "links", "audio_lyrics", "notification_subscribers", "type", "origin",
        "released_year", "size_in_bytes", "source_object", "source", "status", "tag",
        "audio_album_track_number", "width_in_pixels",
    };

    // A fresh space ships these three under English names and the manifests
    // rename them (« État », « Fait », « Échéance »), so a plan declares them.
    private static readonly HashSet<string> Renamed = new(StringComparer.Ordinal) { "status", "done", "due_date" };

    // Not read into a plan's `properties`: the space brings them.
    public static bool IsSystem(string key) => Keys.Contains(key) && !Renamed.Contains(key);

    // A column, sort or filter may name these without the plan declaring them.
    public static bool IsKnown(string key) => Keys.Contains(key);
}
