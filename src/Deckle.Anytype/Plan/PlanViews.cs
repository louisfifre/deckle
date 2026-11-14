using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// What a plan's references resolve to in one live space. The plan never holds an
// Anytype id; the builder fills this in as it creates things, and the reader
// fills it in from what it reads.
internal interface IPlanRefs
{
    string TypeId(string key);
    string? TypeKey(string id);
    string ObjectId(string name);
    string? ObjectName(string id);
}

// The two shapes of a view. The plan form is what a person writes and what the
// reader returns: no id, `$self` for the object a view lives in. The wire form is
// what the API takes and serves: ids on sorts and filters and the template
// placeholder. An option is its name in both forms: the v2 API takes and serves
// the name of a select option in a filter value (measured 2026-11-14), not an id.
//
// Why ids are generated here: a sort or a filter sent without an id read back
// right, then collapsed into the first one when the object was loaded again
// (runbook, 2026-11-13). The server also replaces an id we send with its own
// (measured 2026-11-14), so the id here is not an identity; it is what keeps the
// list from collapsing before the server assigns its own.
internal static class PlanViews
{
    // What the app writes into a template's view in place of the object that
    // will be born from it. The API does not replace it.
    public const string TemplatePlaceholder = "_filter_template_1_";
    // How a plan spells it.
    public const string SelfValue = "$self";

    // A view column the API cannot hide or remove (runbook, Limits). It is left
    // out of both sides, so a view never reads as different because of it.
    private static readonly HashSet<string> UnreachableColumns =
        new(StringComparer.Ordinal) { "lastOpenedDate", "last_opened_date" };

    private static readonly string[] ScalarSettings =
    [
        "alternate_rows", "card_size", "colored_groups", "cover_fit", "cover_property",
        "end_property", "hide_icon", "list_size", "page_size", "wrap_content",
    ];

    public const int MaxSorts = 10;
    public const int MaxFilters = 32;

    public static string NewEntryId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    public static bool IsUnreachableColumn(string property) => UnreachableColumns.Contains(property);

    // ── Plan form → wire form ───────────────────────────────────────────────

    public static JsonObject ToWire(JsonObject view, IPlanRefs refs, string? selfValue = null)
    {
        var wire = new JsonObject { ["name"] = view["name"]?.DeepClone() };
        string type = view["type"]?.GetValue<string>() ?? "table";
        wire["type"] = type;
        if (view["group_by"] is { } group) wire["group_by"] = group.DeepClone();
        foreach (string setting in ScalarSettings)
            if (view[setting] is { } value) wire[setting] = value.DeepClone();

        var filters = new JsonArray();
        foreach (JsonObject filter in (view["filters"] as JsonArray ?? []).OfType<JsonObject>())
            filters.Add(FilterToWire(filter, refs, selfValue));
        wire["filters"] = filters;

        var sorts = new JsonArray();
        foreach (JsonObject sort in (view["sorts"] as JsonArray ?? []).OfType<JsonObject>())
        {
            JsonObject copy = (JsonObject)sort.DeepClone();
            copy["id"] = NewEntryId();
            sorts.Add(copy);
        }
        wire["sorts"] = sorts;

        var columns = new JsonArray();
        foreach (JsonObject column in (view["columns"] as JsonArray ?? []).OfType<JsonObject>())
            columns.Add(column.DeepClone());
        wire["columns"] = columns;
        return wire;
    }

    private static JsonObject FilterToWire(JsonObject filter, IPlanRefs refs, string? selfValue)
    {
        if (filter["filters"] is JsonArray inner)
        {
            var group = new JsonObject
            {
                ["operator"] = filter["operator"]?.DeepClone() ?? "and",
            };
            var children = new JsonArray();
            foreach (JsonObject child in inner.OfType<JsonObject>()) children.Add(FilterToWire(child, refs, selfValue));
            group["filters"] = children;
            return group;
        }

        var wire = new JsonObject();
        foreach ((string key, JsonNode? value) in filter)
            if (key != "value") wire[key] = value?.DeepClone();
        wire["id"] = NewEntryId();
        string property = filter["property"]?.GetValue<string>() ?? "";
        if (filter["value"] is { } plain)
            wire["value"] = ValueToWire(property, plain, refs, selfValue);
        return wire;
    }

    private static JsonNode? ValueToWire(string property, JsonNode value, IPlanRefs refs, string? selfValue)
    {
        if (value is JsonArray array)
        {
            var result = new JsonArray();
            foreach (JsonNode? item in array) result.Add(item is null ? null : ValueToWire(property, item, refs, selfValue));
            return result;
        }
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out string? text))
        {
            if (text == SelfValue) return selfValue ?? TemplatePlaceholder;
        }
        return value.DeepClone();
    }

    // ── Wire form → plan form ───────────────────────────────────────────────

    public static JsonObject FromWire(JsonObject wire, IPlanRefs refs)
    {
        var view = new JsonObject { ["name"] = wire["name"]?.DeepClone() };
        view["type"] = wire["type"]?.GetValue<string>() is { Length: > 0 } t ? t : "table";
        if (wire["group_by"]?.GetValue<string>() is { Length: > 0 } group) view["group_by"] = group;
        foreach (string setting in ScalarSettings)
            if (wire[setting] is { } value) view[setting] = value.DeepClone();

        var filters = new JsonArray();
        foreach (JsonObject filter in (wire["filters"] as JsonArray ?? []).OfType<JsonObject>())
            filters.Add(FilterFromWire(filter, refs));
        view["filters"] = filters;

        var sorts = new JsonArray();
        foreach (JsonObject sort in (wire["sorts"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var copy = new JsonObject { ["property"] = sort["property"]?.DeepClone() };
            copy["direction"] = sort["direction"]?.GetValue<string>() is { Length: > 0 } d ? d : "asc";
            foreach (string extra in new[] { "empty_placement", "include_time", "no_collate", "custom_order" })
                if (sort[extra] is { } v) copy[extra] = v.DeepClone();
            sorts.Add(copy);
        }
        view["sorts"] = sorts;

        var columns = new JsonArray();
        foreach (JsonObject column in (wire["columns"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string property = column["property"]?.GetValue<string>() ?? "";
            if (property.Length == 0 || IsUnreachableColumn(property)) continue;
            var copy = new JsonObject { ["property"] = property };
            if (column["width"] is { } width) copy["width"] = width.DeepClone();
            if (column["hidden"]?.GetValue<bool>() == true) copy["hidden"] = true;
            foreach (string extra in new[] { "aggregation", "align" })
                if (column[extra] is { } v) copy[extra] = v.DeepClone();
            columns.Add(copy);
        }
        view["columns"] = columns;
        return view;
    }

    private static JsonObject FilterFromWire(JsonObject filter, IPlanRefs refs)
    {
        if (filter["filters"] is JsonArray inner)
        {
            var group = new JsonObject { ["operator"] = filter["operator"]?.DeepClone() ?? "and" };
            var children = new JsonArray();
            foreach (JsonObject child in inner.OfType<JsonObject>()) children.Add(FilterFromWire(child, refs));
            group["filters"] = children;
            return group;
        }

        var plan = new JsonObject();
        foreach ((string key, JsonNode? value) in filter)
            if (key is not ("id" or "value" or "nested_property")) plan[key] = value?.DeepClone();
        string property = filter["property"]?.GetValue<string>() ?? "";
        if (filter["value"] is { } raw) plan["value"] = ValueFromWire(property, raw, refs);
        return plan;
    }

    private static JsonNode? ValueFromWire(string property, JsonNode value, IPlanRefs refs)
    {
        if (value is JsonArray array)
        {
            var result = new JsonArray();
            foreach (JsonNode? item in array) result.Add(item is null ? null : ValueFromWire(property, item, refs));
            return result;
        }
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out string? text))
        {
            if (text == TemplatePlaceholder) return SelfValue;
        }
        return value.DeepClone();
    }

    // ── Bodies ──────────────────────────────────────────────────────────────

    // A body in plan form: no block id, no view id, dataview sources by symbol.
    public static JsonArray BodyFromWire(JsonArray? blocks, IPlanRefs refs)
    {
        var result = new JsonArray();
        foreach (JsonObject block in (blocks ?? []).OfType<JsonObject>())
        {
            var plan = new JsonObject();
            foreach ((string key, JsonNode? value) in block)
            {
                if (key is "id" or "fields") continue;
                if (key == "object_id" && block["type"]?.GetValue<string>() == "dataview")
                {
                    string id = value?.GetValue<string>() ?? "";
                    plan["source"] = refs.TypeKey(id) is { } typeKey ? "type:" + typeKey
                        : refs.ObjectName(id) is { } name ? "object:" + name
                        : id;
                    continue;
                }
                if (key == "views" && value is JsonArray views)
                {
                    var planViews = new JsonArray();
                    foreach (JsonObject v in views.OfType<JsonObject>()) planViews.Add(FromWire(v, refs));
                    plan["views"] = planViews;
                    continue;
                }
                if (key == "indent" && value?.GetValue<int>() == 0) continue;
                plan[key] = value?.DeepClone();
            }
            result.Add(plan);
        }
        return result;
    }

    // A body ready to send: dataview sources resolved, every view in wire form.
    public static JsonArray BodyToWire(JsonArray blocks, IPlanRefs refs)
    {
        var result = new JsonArray();
        foreach (JsonObject block in blocks.OfType<JsonObject>())
        {
            var wire = new JsonObject();
            foreach ((string key, JsonNode? value) in block)
            {
                if (key == "source" && block["type"]?.GetValue<string>() == "dataview")
                {
                    string source = value?.GetValue<string>() ?? "";
                    wire["object_id"] = source.StartsWith("type:", StringComparison.Ordinal)
                        ? refs.TypeId(source["type:".Length..])
                        : source.StartsWith("object:", StringComparison.Ordinal)
                            ? refs.ObjectId(source["object:".Length..])
                            : source;
                    continue;
                }
                if (key == "views" && value is JsonArray views)
                {
                    var wireViews = new JsonArray();
                    foreach (JsonObject v in views.OfType<JsonObject>()) wireViews.Add(ToWire(v, refs));
                    wire["views"] = wireViews;
                    continue;
                }
                if (key == "indent")
                {
                    // insert_blocks says relative_indent; a creation body says indent.
                    wire["indent"] = value?.DeepClone();
                    continue;
                }
                wire[key] = value?.DeepClone();
            }
            result.Add(wire);
        }
        return result;
    }
}
