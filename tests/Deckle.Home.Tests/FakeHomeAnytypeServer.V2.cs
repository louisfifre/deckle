using System.Text.Json.Nodes;
using Deckle.Home;

namespace Deckle.Home.Tests;

// The v2 routes the structure export, write and read-back use, over the same
// fake space: type settings and views, templates, collection views, widgets.
// It keeps the rules the real API showed in the probe that matter to the
// tests: a patch of property_definitions replaces the whole list, `columns`
// patches per property key and leaves the others alone, `set` merges but
// sorts and filters replace, and a property key the space does not hold is
// refused (a column, a sort, a filter, a group, a template value, a property
// block), as is a template id or a widget target that does not exist.
internal sealed partial class FakeHomeAnytypeServer
{
    private static readonly string[] BuiltInPropertyKeys =
    [
        "name", "type", "description", "created_date", "creator", "last_modified_date", "last_modified_by",
        "lastOpenedDate", "last_opened_date", "backlinks", "links", "tag", "done", "added_date",
    ];

    private readonly Dictionary<string, JsonObject> _v2Types = new(StringComparer.Ordinal);
    private readonly List<JsonObject> _v2Templates = [];
    private readonly Dictionary<string, JsonArray> _v2CollectionViews = new(StringComparer.Ordinal);
    private readonly List<JsonObject> _v2Widgets = [];
    private int _v2Next;

    // A type as the old space served it, or as a test wants the target to
    // hold it: settings, the property list with its sections, the views.
    public void SetTypeStructure(
        string key, string? defaultView, string? defaultTemplate, JsonArray propertyDefinitions, JsonArray views)
    {
        var settings = new JsonObject
        {
            ["layout"] = "basic",
            ["api_key"] = key,
            ["plural_name"] = key + "s",
            ["property_definitions"] = propertyDefinitions,
        };
        if (defaultView is not null) settings["default_view"] = defaultView;
        if (defaultTemplate is not null) settings["default_template"] = defaultTemplate;

        var dataviewProperties = new JsonArray(new JsonObject { ["property"] = "name", ["format"] = "text" });
        foreach (JsonObject definition in propertyDefinitions.OfType<JsonObject>())
            dataviewProperties.Add(new JsonObject
            {
                ["property"] = definition["property"]!.GetValue<string>(),
                ["format"] = definition["format"]?.GetValue<string>() ?? "text",
            });

        _v2Types[key] = new JsonObject
        {
            ["id"] = TypeIdPrefix + key,
            ["kind"] = "object_type",
            ["internal_key"] = "ik-" + key,
            ["type_settings"] = settings,
            ["blocks"] = new JsonArray(new JsonObject
            {
                ["id"] = "dataview",
                ["type"] = "dataview",
                ["properties"] = dataviewProperties,
                ["views"] = views,
            }),
        };
    }

    // Every type of the manifest, as a fresh space holds it: no default view,
    // no default template, no section, one default view showing every property.
    public void SeedDefaultTypeStructure()
    {
        foreach (JsonObject spec in ((JsonArray)_schemaManifest["types"]!).OfType<JsonObject>())
        {
            var definitions = new JsonArray();
            var columns = new JsonArray(new JsonObject { ["property"] = "name", ["width"] = 200 });
            foreach (JsonNode? property in (JsonArray)spec["properties"]!)
            {
                string propertyKey = property!.GetValue<string>();
                definitions.Add(new JsonObject
                {
                    ["property"] = propertyKey,
                    ["internal_key"] = "ik-" + propertyKey,
                    ["name"] = propertyKey,
                    ["format"] = "text",
                });
                columns.Add(new JsonObject { ["property"] = propertyKey, ["width"] = 100 });
            }
            SetTypeStructure(
                spec["key"]!.GetValue<string>(), null, null, definitions,
                new JsonArray(new JsonObject { ["id"] = "default", ["name"] = "All", ["columns"] = columns }));
        }
    }

    // A type the space no longer has (the old space had no product, only its
    // errand).
    public void RemoveSchemaType(string key)
    {
        JsonArray types = (JsonArray)_schemaManifest["types"]!;
        if (types.OfType<JsonObject>().FirstOrDefault(value => value["key"]?.GetValue<string>() == key) is { } found)
            types.Remove(found);
        _v2Types.Remove(key);
    }

    public JsonObject? TypeStructure(string key) =>
        _v2Types.TryGetValue(key, out JsonObject? document) ? (JsonObject)document.DeepClone() : null;

    public JsonObject? TemplateDocument(string id) =>
        _v2Templates.FirstOrDefault(value => value["id"]!.GetValue<string>() == id) is { } found
            ? (JsonObject)found.DeepClone()
            : null;

    public IReadOnlyList<JsonObject> Templates() => _v2Templates.Select(value => (JsonObject)value.DeepClone()).ToArray();

    // A template of the old space: the document as its reads serve it.
    public void AddTemplateDocument(JsonObject document) => _v2Templates.Add((JsonObject)document.DeepClone());

    public void SetCollectionViews(string collectionId, JsonArray views) => _v2CollectionViews[collectionId] = views;

    public JsonArray? CollectionViews(string collectionId) =>
        _v2CollectionViews.TryGetValue(collectionId, out JsonArray? views) ? (JsonArray)views.DeepClone() : null;

    public void AddWidget(string id, string scope, string target, string layout, int? limit = null, string? viewId = null)
    {
        var widget = new JsonObject { ["id"] = id, ["scope"] = scope, ["target"] = target, ["layout"] = layout };
        if (limit is int count) widget["limit"] = count;
        if (viewId is not null) widget["view_id"] = viewId;
        _v2Widgets.Add(widget);
    }

    public void RemoveWidgets() => _v2Widgets.Clear();

    public IReadOnlyList<JsonObject> Widgets() => _v2Widgets.Select(value => (JsonObject)value.DeepClone()).ToArray();

    private void AddStructureOnlyTypes(JsonArray data)
    {
        foreach (string key in _v2Types.Keys)
        {
            if (data.OfType<JsonObject>().Any(value => value["key"]?.GetValue<string>() == key)) continue;
            data.Add(new JsonObject
            {
                ["id"] = TypeIdPrefix + key, ["key"] = key, ["name"] = key, ["plural_name"] = key + "s",
                ["layout"] = "basic", ["properties"] = new JsonArray(),
            });
        }
    }

    private (int Status, string Json) HandleV2(
        string method, string path, string body, System.Collections.Specialized.NameValueCollection query)
    {
        string[] segments = path.Split('/');
        JsonObject? payload = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;

        if (method == "GET" && segments is ["types", var typeKey])
            return _v2Types.TryGetValue(typeKey, out JsonObject? type)
                ? (200, type.ToJsonString())
                : V2Error(404, "not_found", "type not found");
        if (method == "PATCH" && segments is ["types", var patchedKey])
            return _v2Types.TryGetValue(patchedKey, out JsonObject? patched)
                ? PatchType(patchedKey, patched, payload!)
                : V2Error(404, "not_found", "type not found");

        if (method == "GET" && segments is ["templates"])
        {
            var rows = new JsonArray();
            foreach (JsonObject template in _v2Templates)
                rows.Add(new JsonObject
                {
                    ["id"] = template["id"]!.DeepClone(),
                    ["name"] = template["properties"]?["name"]?.DeepClone(),
                });
            return (200, Slice(rows, query).ToJsonString());
        }
        if (method == "POST" && segments is ["templates"]) return CreateTemplate(payload!);
        if (method == "GET" && segments is ["objects", var objectId])
            return _v2Templates.FirstOrDefault(value => value["id"]!.GetValue<string>() == objectId) is { } found
                ? (200, found.ToJsonString())
                : V2Error(404, "not_found", "object not found");

        if (method == "GET" && segments is ["collections", var collectionId, "views"])
        {
            if (!IsCollection(collectionId)) return V2Error(404, "not_found", "collection not found");
            JsonArray views = CollectionViewsOf(collectionId);
            JsonObject page = Slice(views, query);
            return (200, new JsonObject
            {
                ["data"] = page["data"]!.DeepClone(),
                ["total"] = views.Count,
                ["has_more"] = page["pagination"]!["has_more"]!.GetValue<bool>(),
            }.ToJsonString());
        }
        if (method == "PATCH" && segments is ["objects", var patchedId] && IsCollection(patchedId))
        {
            var created = new JsonObject();
            JsonArray views = CollectionViewsOf(patchedId);
            string? problem = ApplyViewOps(views, payload?["ops"] as JsonArray, ["name", "type", "created_date"], created);
            return problem is null
                ? (200, new JsonObject { ["etag"] = "e", ["created_views"] = created }.ToJsonString())
                : V2Error(400, "validation_failed", problem);
        }

        if (method == "GET" && segments is ["widgets"])
            return (200, new JsonObject
            {
                ["data"] = new JsonArray(_v2Widgets.Select(widget => (JsonNode?)widget.DeepClone()).ToArray()),
                ["total"] = _v2Widgets.Count,
                ["has_more"] = false,
            }.ToJsonString());
        if (method == "POST" && segments is ["widgets"]) return CreateWidget(payload!);

        return V2Error(404, "not_found", "no such v2 route in the fake");
    }

    private static (int Status, string Json) V2Error(int status, string code, string message) =>
        (status, new JsonObject { ["status"] = status, ["code"] = code, ["message"] = message }.ToJsonString());

    private bool IsCollection(string id) =>
        _objects.Any(value => value["id"]?.GetValue<string>() == id
            && (value["layout"]?.GetValue<string>() == "collection"
                || value["type"]?["layout"]?.GetValue<string>() == "collection"));

    private JsonArray CollectionViewsOf(string collectionId)
    {
        if (_v2CollectionViews.TryGetValue(collectionId, out JsonArray? views)) return views;
        return _v2CollectionViews[collectionId] = new JsonArray(new JsonObject
        {
            ["id"] = "default",
            ["name"] = "All",
            ["columns"] = new JsonArray(
                new JsonObject { ["property"] = "name" },
                new JsonObject { ["property"] = "type" },
                new JsonObject { ["property"] = "created_date", ["hidden"] = true }),
        });
    }

    private bool KnownPropertyKey(string key) =>
        BuiltInPropertyKeys.Contains(key, StringComparer.Ordinal)
        || ((JsonArray)_schemaManifest["properties"]!).OfType<JsonObject>()
            .Any(value => value["key"]?.GetValue<string>() == key);

    private (int Status, string Json) PatchType(string key, JsonObject type, JsonObject payload)
    {
        JsonObject settings = (JsonObject)type["type_settings"]!;
        if (payload["default_template"] is JsonValue template)
        {
            string id = template.GetValue<string>();
            if (id.Length > 0 && _v2Templates.All(value => value["id"]!.GetValue<string>() != id))
                return V2Error(400, "validation_failed", $"default_template: unknown template {id}");
            settings["default_template"] = id;
        }
        if (payload["default_view"] is JsonValue view) settings["default_view"] = view.GetValue<string>();

        if (payload["property_definitions"] is JsonArray definitions)
        {
            JsonArray current = (JsonArray)settings["property_definitions"]!;
            var replaced = new JsonArray();
            foreach (JsonObject entry in definitions.OfType<JsonObject>())
            {
                string property = entry["property"]?.GetValue<string>() ?? "";
                if (!KnownPropertyKey(property))
                    return V2Error(400, "validation_failed", $"property_definitions: unknown property {property}");
                JsonObject kept = current.OfType<JsonObject>()
                    .FirstOrDefault(value => value["property"]!.GetValue<string>() == property) is { } old
                    ? (JsonObject)old.DeepClone()
                    : new JsonObject { ["property"] = property, ["name"] = property, ["format"] = "text" };
                kept.Remove("section");
                if (entry["section"] is JsonValue section) kept["section"] = section.GetValue<string>();
                replaced.Add(kept);
            }
            settings["property_definitions"] = replaced;
        }

        var created = new JsonObject();
        if (payload["ops"] is JsonArray ops)
        {
            JsonObject dataview = (JsonObject)((JsonArray)type["blocks"]!)[0]!;
            string[] baseline = ((JsonArray)dataview["properties"]!).OfType<JsonObject>()
                .Select(value => value["property"]!.GetValue<string>()).ToArray();
            string? problem = ApplyViewOps((JsonArray)dataview["views"]!, ops, baseline, created);
            if (problem is not null) return V2Error(400, "validation_failed", problem);
        }
        return (200, new JsonObject { ["id"] = type["id"]!.DeepClone(), ["key"] = key, ["etag"] = "e", ["created_views"] = created }
            .ToJsonString());
    }

    // insert_view and update_view over one list of views. Returns the refusal
    // text, or null when every op applied.
    private string? ApplyViewOps(JsonArray views, JsonArray? ops, string[] baseline, JsonObject created)
    {
        foreach ((JsonNode? node, int index) in (ops ?? []).Select((node, index) => (node, index)))
        {
            if (node is not JsonObject op) return $"ops[{index}]: not an object";
            string name = op["op"]?.GetValue<string>() ?? "";
            JsonObject target;
            if (name == "insert_view")
            {
                target = new JsonObject
                {
                    ["id"] = "v" + Interlocked.Increment(ref _v2Next),
                    ["name"] = op["name"]?.GetValue<string>() ?? "",
                    ["columns"] = new JsonArray(baseline.Select(property =>
                        (JsonNode?)new JsonObject { ["property"] = property }).ToArray()),
                };
                if (op["position"]?.GetValue<string>() == "first") views.Insert(0, target); else views.Add(target);
                created[$"/ops/{index}"] = target["id"]!.GetValue<string>();
            }
            else if (name == "delete_view")
            {
                string wanted = op["view"]?.GetValue<string>() ?? "";
                JsonObject? doomed = views.OfType<JsonObject>().FirstOrDefault(value => value["id"]!.GetValue<string>() == wanted);
                if (doomed is null) return $"ops[{index}]: view {wanted} not found";
                views.Remove(doomed);
                continue;
            }
            else if (name == "update_view")
            {
                string wanted = op["view"]?.GetValue<string>() ?? "default";
                JsonObject? found = views.OfType<JsonObject>().FirstOrDefault(value => value["id"]!.GetValue<string>() == wanted)
                    ?? (wanted == "default" ? views.OfType<JsonObject>().FirstOrDefault() : null);
                if (found is null) return $"ops[{index}]: view {wanted} not found";
                target = found;
            }
            else return $"ops[{index}]: unknown op {name}";

            if (op["set"] is JsonObject set)
                foreach ((string field, JsonNode? value) in set)
                {
                    string? problem = CheckViewField(field, value);
                    if (problem is not null) return $"ops[{index}]: {problem}";
                    if (value is null) target.Remove(field); else target[field] = value.DeepClone();
                }
            if (op["columns"] is JsonObject columns)
                foreach ((string property, JsonNode? patch) in columns)
                {
                    if (!KnownPropertyKey(property)) return $"ops[{index}]: columns: unknown property {property}";
                    JsonArray list = (JsonArray)target["columns"]!;
                    JsonObject? column = list.OfType<JsonObject>().FirstOrDefault(value =>
                        value["property"]!.GetValue<string>() == property);
                    if (patch is null)
                    {
                        if (column is not null) list.Remove(column);
                        continue;
                    }
                    if (column is null) list.Add(column = new JsonObject { ["property"] = property });
                    if (patch["hidden"] is JsonValue hidden)
                    {
                        if (hidden.GetValue<bool>()) column["hidden"] = true; else column.Remove("hidden");
                    }
                    if (patch["width"] is JsonValue width) column["width"] = width.DeepClone();
                    if (patch["aggregation"] is JsonValue aggregation) column["aggregation"] = aggregation.DeepClone();
                }
        }
        return null;
    }

    private string? CheckViewField(string field, JsonNode? value)
    {
        if (field == "group_by" && value is JsonValue group && !KnownPropertyKey(group.GetValue<string>()))
            return $"group_by: unknown property {group.GetValue<string>()}";
        if (field == "sorts" && value is JsonArray sorts)
            foreach (JsonObject sort in sorts.OfType<JsonObject>())
                if (!KnownPropertyKey(sort["property"]?.GetValue<string>() ?? ""))
                    return $"sorts: unknown property {sort["property"]}";
        if (field == "filters" && value is JsonArray filters)
            foreach (JsonNode? filter in filters)
                if (UnknownFilterProperty(filter) is { } unknown) return $"filters: unknown property {unknown}";
        return null;
    }

    private string? UnknownFilterProperty(JsonNode? filter)
    {
        if (filter is not JsonObject node) return null;
        if (node["filters"] is JsonArray nested)
            return nested.Select(UnknownFilterProperty).FirstOrDefault(found => found is not null);
        string property = node["property"]?.GetValue<string>() ?? "";
        return KnownPropertyKey(property) ? null : property;
    }

    private (int Status, string Json) CreateTemplate(JsonObject payload)
    {
        string templateFor = payload["template_for"]?.GetValue<string>() ?? "";
        if (!_v2Types.ContainsKey(templateFor)) return V2Error(400, "validation_failed", $"template_for: unknown type {templateFor}");
        foreach ((string key, JsonNode? _) in payload["properties"] as JsonObject ?? [])
            if (key != "name" && !KnownPropertyKey(key))
                return V2Error(400, "validation_failed", $"properties: unknown property {key}");

        JsonArray blocks = payload["blocks"] as JsonArray ?? [];
        foreach (JsonObject block in blocks.OfType<JsonObject>())
        {
            if (block["fields"] is not null || block["source"] is not null)
                return V2Error(400, "validation_failed", "blocks: output-only member sent");
            if (block["type"]?.GetValue<string>() == "property" && !KnownPropertyKey(block["property"]?.GetValue<string>() ?? ""))
                return V2Error(400, "validation_failed", $"blocks: unknown property {block["property"]}");
            if (block["type"]?.GetValue<string>() == "dataview")
                foreach (JsonObject view in (block["views"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (view["groups"] is not null || view["object_orders"] is not null)
                        return V2Error(400, "validation_failed", "views: output-only member sent");
                    if (view["filters"] is JsonArray filters && CheckViewField("filters", filters) is { } problem)
                        return V2Error(400, "validation_failed", problem);
                }
        }

        string id = "template-" + Interlocked.Increment(ref _v2Next);
        var document = new JsonObject
        {
            ["kind"] = "template",
            ["id"] = id,
            ["type"] = "template",
            ["template_for"] = templateFor,
            ["properties"] = payload["properties"]?.DeepClone() ?? new JsonObject(),
            ["blocks"] = blocks.DeepClone(),
        };
        _v2Templates.Add(document);
        return (200, new JsonObject { ["id"] = id, ["type"] = "template", ["etag"] = "e" }.ToJsonString());
    }

    private (int Status, string Json) CreateWidget(JsonObject payload)
    {
        string target = payload["target"]?.GetValue<string>() ?? "";
        bool exists = _objects.Any(value => value["id"]?.GetValue<string>() == target)
            || _v2Types.Values.Any(value => value["id"]!.GetValue<string>() == target)
            || _v2Templates.Any(value => value["id"]!.GetValue<string>() == target);
        if (!exists) return V2Error(400, "validation_failed", $"target: {target} not found");
        if (payload["scope"]?.GetValue<string>() is not ("space" or "personal"))
            return V2Error(400, "validation_failed", "scope: required");

        var widget = new JsonObject
        {
            ["id"] = "widget-" + Interlocked.Increment(ref _v2Next),
            ["scope"] = payload["scope"]!.DeepClone(),
            ["target"] = target,
            ["layout"] = payload["layout"]?.DeepClone() ?? "link",
        };
        if (payload["limit"] is JsonValue limit) widget["limit"] = limit.DeepClone();
        if (payload["view_id"] is JsonValue viewId) widget["view_id"] = viewId.DeepClone();
        if (payload["position"]?.GetValue<string>() == "first") _v2Widgets.Insert(0, widget); else _v2Widgets.Add(widget);
        return (200, widget.ToJsonString());
    }
}
