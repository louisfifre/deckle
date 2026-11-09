using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Deckle.Home;

// The structure of the source, put on the new model: pure, no I/O, so every
// rule is unit-tested without a space.
//
// Keys. A type key goes through the exporter's bridge (errand → product,
// floor → zone). A property key goes through the same bridge the objects used
// (documents → manual, errand_category → product_category, memory_size_gb →
// memory_size, memory_frequency_mhz → memory_frequency, switch_kind →
// switch_nature, floor → zone). A key the new manifest does not hold
// (notes, power_watts, condition, aisle…) is DROPPED, never guessed: from the
// columns, sorts, group, filters of a view, from a template's values and
// property blocks, from a type's sections. Anytype's built-in keys (name,
// created_date, done…) pass. A property the manifest does not attach to the
// type a view or a template is for is dropped too: the new type does not
// carry it.
//
// Ids. Every id a structure names is looked up in `ids` (old → new: objects
// from the replay, types, templates, section collections). A value that
// looks like an object id (bafy…) and is not in the map is dropped, as is
// the member that carried it. « This object » (`_filter_template_1_`) and
// every other literal pass untouched.
//
// Nothing is dropped silently: each drop is counted under its kind in
// `Dropped` (counts only, safe to print) and described in `Details` (names,
// for the file beside the sheet).
internal sealed partial class HomeStructureRemap(IReadOnlyDictionary<string, string> ids)
{
    // How a filter says « this object » in a template and in what is made
    // from it; the app resolves it when it renders.
    public const string ThisObjectToken = "_filter_template_1_";

    // Anytype's own properties: they carry the same key on any space.
    internal static readonly IReadOnlySet<string> BuiltInKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "name", "type", "description", "created_date", "creator", "last_modified_date", "last_modified_by",
        "lastOpenedDate", "last_opened_date", "last_used_date", "added_date", "backlinks", "links", "mentions",
        "tag", "done",
    };

    // What a template carries that is not Home's information: bookkeeping of
    // the app. Dropped without a count.
    internal static readonly IReadOnlySet<string> TemplateSystemKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "created_date", "creator", "last_modified_by", "last_modified_date", "last_opened_date", "layout",
        "resolved_layout", "target_object_type", "source_object", "tag", "origin", "links", "backlinks",
        "mentions", "added_date", "icon", "cover",
    };

    // The members of a view that `set` takes on an insert_view / update_view.
    internal static readonly IReadOnlySet<string> ViewSettings = new HashSet<string>(StringComparer.Ordinal)
    {
        "alternate_rows", "card_size", "colored_groups", "cover_fit", "cover_property", "default_template_id",
        "default_type_id", "end_property", "filters", "group_by", "hide_icon", "list_size", "page_size",
        "sorts", "type", "wrap_content",
    };

    public SortedDictionary<string, int> Dropped { get; } = new(StringComparer.Ordinal);

    public List<HomeStructureDrop> Details { get; } = [];

    // ── Keys ────────────────────────────────────────────────────────────

    public static string TypeKey(string sourceKey) => HomeSpaceExporter.BridgeType(sourceKey);

    // The new key of a source property, or null when it does not exist on the
    // new model (or, with a type, is not attached to it).
    public static string? PropertyKey(string sourceKey, string? typeKey)
    {
        if (sourceKey.Length == 0 || sourceKey == HomeSpaceExporter.NotesKey
            || HomeSpaceExporter.DeadKeys.Contains(sourceKey))
            return null;
        string key = HomeSpaceExporter.PropertyRenames.TryGetValue(sourceKey, out string? renamed) ? renamed : sourceKey;
        if (HomeSchema.RequiredProperties.ContainsKey(key))
            return typeKey is null || HomeSchema.IsAllowedProperty(typeKey, key) ? key : null;
        return BuiltInKeys.Contains(sourceKey) ? sourceKey : null;
    }

    // `type-<key>` is how a dataview names a type as its source.
    public static string TypeSource(string sourceKey) => "type-" + TypeKey(sourceKey);

    // ── Views ───────────────────────────────────────────────────────────

    // A view as the AnyBlock document spells it (a template's dataview),
    // every property remapped and every output-only member left out.
    public JsonObject CleanView(JsonObject view, string? typeKey, string where)
    {
        string label = $"{where}, view « {HomeObjectJson.String(view, "name")} »";
        var result = new JsonObject();
        foreach ((string field, JsonNode? value) in view)
        {
            switch (field)
            {
                case "id" or "groups" or "object_orders":
                    break;
                case "columns" when value is JsonArray columns:
                    result[field] = CleanColumns(columns, typeKey, label);
                    break;
                case "sorts" when value is JsonArray sorts:
                    result[field] = CleanSorts(sorts, typeKey, label);
                    break;
                case "filters" when value is JsonArray filters:
                    result[field] = CleanFilters(filters, typeKey, label);
                    break;
                case "group_by" or "cover_property" or "end_property" when value is JsonValue:
                {
                    string source = HomeObjectJson.String(view, field);
                    if (PropertyKey(source, typeKey) is { } mapped) result[field] = mapped;
                    else Drop(field == "group_by" ? "group_by" : "view_property", label, source);
                    break;
                }
                case "default_template_id" or "default_type_id" when value is JsonValue:
                {
                    string source = HomeObjectJson.String(view, field);
                    if (ids.TryGetValue(source, out string? mapped)) result[field] = mapped;
                    else Drop("id", label, source);
                    break;
                }
                default:
                    result[field] = value?.DeepClone();
                    break;
            }
        }
        return result;
    }

    private JsonArray CleanColumns(JsonArray columns, string? typeKey, string where)
    {
        var result = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonObject column in columns.OfType<JsonObject>())
        {
            string source = HomeObjectJson.String(column, "property");
            if (PropertyKey(source, typeKey) is not { } key)
            {
                Drop("column", where, source);
                continue;
            }
            if (!seen.Add(key)) continue;
            var kept = (JsonObject)column.DeepClone();
            kept["property"] = key;
            result.Add(kept);
        }
        return result;
    }

    private JsonArray CleanSorts(JsonArray sorts, string? typeKey, string where)
    {
        var result = new JsonArray();
        foreach (JsonObject sort in sorts.OfType<JsonObject>())
        {
            string source = HomeObjectJson.String(sort, "property");
            if (PropertyKey(source, typeKey) is not { } key)
            {
                Drop("sort", where, source);
                continue;
            }
            var kept = (JsonObject)sort.DeepClone();
            kept.Remove("id");
            kept["property"] = key;
            result.Add(kept);
        }
        return result;
    }

    private JsonArray CleanFilters(JsonArray filters, string? typeKey, string where)
    {
        var result = new JsonArray();
        foreach (JsonObject filter in filters.OfType<JsonObject>())
            if (CleanFilter(filter, typeKey, where) is { } kept) result.Add(kept);
        return result;
    }

    // A group node keeps the members that survive and goes when none does; a
    // leaf goes when its property is dropped or every value it named is.
    private JsonObject? CleanFilter(JsonObject filter, string? typeKey, string where)
    {
        if (filter["filters"] is JsonArray nested)
        {
            JsonArray kept = CleanFilters(nested, typeKey, where);
            if (kept.Count == 0) return null;
            var group = (JsonObject)filter.DeepClone();
            group["filters"] = kept;
            return group;
        }

        string source = HomeObjectJson.String(filter, "property");
        if (PropertyKey(source, typeKey) is not { } key)
        {
            Drop("filter", where, source);
            return null;
        }

        var leaf = new JsonObject();
        foreach ((string field, JsonNode? value) in filter)
        {
            if (field is "id" or "nested_property") continue;
            leaf[field] = field == "property" ? key : value?.DeepClone();
        }
        if (filter["value"] is JsonNode original)
        {
            JsonNode? mapped = FilterValue(key, original, where, out bool lost);
            if (lost)
            {
                Drop("filter", where, source);
                return null;
            }
            leaf["value"] = mapped;
        }
        return leaf;
    }

    // `lost` is true when the value named things and none of them survives.
    private JsonNode? FilterValue(string key, JsonNode value, string where, out bool lost)
    {
        lost = false;
        if (value is JsonArray array)
        {
            var kept = new JsonArray();
            foreach (JsonNode? item in array)
                if (item is JsonValue scalar && MapScalar(key, scalar, where) is { } mapped) kept.Add(mapped);
                else if (item is not JsonValue) kept.Add(item?.DeepClone());
            lost = array.Count > 0 && kept.Count == 0;
            return kept;
        }
        if (value is JsonValue single)
        {
            JsonNode? mapped = MapScalar(key, single, where);
            lost = mapped is null;
            return mapped;
        }
        return value.DeepClone();
    }

    // A string that names an object, a type or a template is looked up; one
    // that names an option of a select is read through the manifest's
    // options; anything else passes. Null: the value is dropped.
    private JsonNode? MapScalar(string key, JsonValue value, string where)
    {
        if (!value.TryGetValue(out string? text) || text is null) return value.DeepClone();
        if (text == ThisObjectToken) return JsonValue.Create(text);
        if (ids.TryGetValue(text, out string? mapped)) return JsonValue.Create(mapped);
        if (LooksLikeObjectId(text))
        {
            Drop("id", where, text);
            return null;
        }
        if (HomeSchema.RequiredProperties.TryGetValue(key, out string? format) && format is "select" or "multi_select")
        {
            if (OptionName(key, text) is { } option) return JsonValue.Create(option);
            Drop("option", where, $"{key}: {text}");
            return null;
        }
        return JsonValue.Create(text);
    }

    // The option name the new space carries for a source option name: the
    // manifest option it stands for (under its key, its French label or its
    // manifest name); a name the manifest does not seed stays as it is on an
    // open vocabulary, and is unknown (null) on a closed one.
    public static string? OptionName(string propertyKey, string name)
    {
        string trimmed = name.Trim();
        if (HomeSchema.TargetSeededVocabularies.TryGetValue(propertyKey, out IReadOnlyList<string>? keys))
            foreach (string key in keys)
                if (HomeSchema.OptionAliases(propertyKey, key).Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    return ManifestOptionName(propertyKey, key) ?? trimmed;
        return HomeSchema.ClosedVocabularies.ContainsKey(propertyKey) ? null : trimmed;
    }

    private static string? ManifestOptionName(string propertyKey, string optionKey) =>
        (HomeSchemaTargetData.Manifest["properties"]!.AsArray().OfType<JsonObject>()
            .FirstOrDefault(property => property["key"]!.GetValue<string>() == propertyKey)?["tags"] as JsonArray)
        ?.OfType<JsonObject>().FirstOrDefault(tag => tag["key"]!.GetValue<string>() == optionKey)?["name"]
        ?.GetValue<string>();

    public static bool LooksLikeObjectId(string text) => ObjectIdPattern().IsMatch(text);

    [GeneratedRegex(@"^bafy[a-z0-9]{20,}$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectIdPattern();

    // The view as the ops take it: what `set` merges, and the column patches
    // by property key. Every visible column carries hidden:false so a column
    // the target had hidden comes back; sorts and filters are always sent
    // (empty when the source had none) because `set` merges and the target's
    // default view brings sorts of its own.
    public HomeViewPlan ViewPlan(JsonObject view, string? typeKey, string where)
    {
        JsonObject cleaned = CleanView(view, typeKey, where);
        var set = new JsonObject();
        foreach ((string field, JsonNode? value) in cleaned)
            if (ViewSettings.Contains(field)) set[field] = value?.DeepClone();
        set["type"] ??= "table";
        set["sorts"] ??= new JsonArray();
        set["filters"] ??= new JsonArray();

        var columns = new JsonObject();
        var visible = new List<string>();
        foreach (JsonObject column in (cleaned["columns"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string key = HomeObjectJson.String(column, "property");
            bool hidden = column["hidden"] is JsonValue flag && flag.TryGetValue(out bool value) && value;
            var patch = new JsonObject { ["hidden"] = hidden };
            if (column["width"] is JsonValue width) patch["width"] = width.DeepClone();
            if (column["aggregation"] is JsonValue aggregation) patch["aggregation"] = aggregation.DeepClone();
            if (column["align"] is JsonValue align) patch["align"] = align.DeepClone();
            columns[key] = patch;
            if (!hidden) visible.Add(key);
        }

        return new HomeViewPlan(
            HomeObjectJson.String(view, "id"), HomeObjectJson.String(view, "name"), set, columns, visible);
    }

    // ── Type settings and views ─────────────────────────────────────────

    public HomeTypePlan TypePlan(HomeStructureType type)
    {
        string key = TypeKey(type.Key);
        string where = $"type {key}";
        var properties = new List<HomeSectionPlan>();
        foreach (HomeStructureProperty definition in type.Properties)
        {
            if (PropertyKey(definition.Property, key) is { } mapped)
                properties.Add(new HomeSectionPlan(mapped, definition.Section));
            else if (definition.Section is not null)
                Drop("section", where, definition.Property);
        }

        var views = type.Views.OfType<JsonObject>().Select(view => ViewPlan(view, key, where)).ToList();
        return new HomeTypePlan(type.Key, key, type.TypeId, type.DefaultView, type.DefaultTemplate, properties, views);
    }

    public List<HomeViewPlan> CollectionViewPlans(HomeStructureCollection collection) =>
        collection.Views.OfType<JsonObject>()
            .Select(view => ViewPlan(view, null, $"collection « {collection.Name} »")).ToList();

    // ── Templates ───────────────────────────────────────────────────────

    // The body POST templates takes, or null for a template of a type the new
    // model does not hold.
    public HomeTemplatePlan? TemplatePlan(HomeStructureTemplate template)
    {
        string key = TypeKey(template.TemplateFor);
        if (!HomeSchema.CreatableTypes.Contains(key, StringComparer.Ordinal))
        {
            Drop("template", $"template « {template.Name} »", template.TemplateFor);
            return null;
        }
        string where = $"template « {template.Name} »";

        var properties = new JsonObject();
        foreach ((string source, JsonNode? value) in template.Document["properties"] as JsonObject ?? [])
        {
            if (source == "name")
            {
                properties["name"] = value?.DeepClone();
                continue;
            }
            if (TemplateSystemKeys.Contains(source)) continue;
            if (PropertyKey(source, key) is not { } mapped || !HomeSchema.RequiredProperties.TryGetValue(mapped, out string? format))
            {
                Drop("template_property", where, source);
                continue;
            }
            JsonNode? kept = TemplateValue(mapped, format, value, where);
            if (kept is not null) properties[mapped] = kept;
        }

        var blocks = new JsonArray();
        foreach (JsonObject block in (template.Document["blocks"] as JsonArray ?? []).OfType<JsonObject>())
            if (TemplateBlock(block, key, where) is { } cleaned) blocks.Add(cleaned);
        UnwrapStrayColumns(blocks, where);

        var body = new JsonObject
        {
            ["kind"] = "template",
            ["type"] = "template",
            ["template_for"] = key,
            ["properties"] = properties,
            ["blocks"] = blocks,
        };
        return new HomeTemplatePlan(template.OldId, HomeObjectJson.String(properties, "name"), key, body);
    }

    // A value as its format takes it on the new space: relations through the
    // id map, options through the manifest, files not at all (they do not
    // travel), the rest as it is. Null: nothing of it survives.
    private JsonNode? TemplateValue(string key, string format, JsonNode? value, string where)
    {
        if (value is null) return null;
        IEnumerable<JsonNode?> items = value is JsonArray array ? array : new JsonNode?[] { value };
        switch (format)
        {
            case "files":
                Drop("template_property", where, key);
                return null;
            case "objects":
            {
                var kept = new JsonArray();
                foreach (JsonNode? item in items)
                    if (item is JsonValue scalar && scalar.TryGetValue(out string? id) && id is not null)
                    {
                        if (ids.TryGetValue(id, out string? mapped)) kept.Add(mapped);
                        else Drop("id", where, id);
                    }
                return kept.Count > 0 ? kept : null;
            }
            case "select" or "multi_select":
            {
                var kept = new List<JsonNode>();
                foreach (JsonNode? item in items)
                    if (item is JsonValue scalar && scalar.TryGetValue(out string? name) && !string.IsNullOrWhiteSpace(name))
                    {
                        if (OptionName(key, name) is { } mapped) kept.Add(JsonValue.Create(mapped)!);
                        else Drop("option", where, $"{key}: {name}");
                    }
                if (kept.Count == 0) return null;
                return value is JsonArray ? new JsonArray(kept.ToArray()) : kept[0];
            }
            default:
                return value.DeepClone();
        }
    }

    // One block of the template's flat list, or null when it cannot travel.
    private JsonObject? TemplateBlock(JsonObject block, string typeKey, string where)
    {
        string kind = HomeObjectJson.String(block, "type");
        if (kind is "file" or "image" or "video" or "audio" or "pdf")
        {
            Drop("block", where, kind);
            return null;
        }

        var result = (JsonObject)block.DeepClone();
        result.Remove("id");
        result.Remove("fields");
        result.Remove("source");

        switch (kind)
        {
            case "property":
            {
                string source = HomeObjectJson.String(block, "property");
                if (PropertyKey(source, typeKey) is not { } key)
                {
                    Drop("property_block", where, source);
                    return null;
                }
                result["property"] = key;
                return result;
            }
            case "link" or "bookmark" when HomeObjectJson.String(block, "object_id") is { Length: > 0 } target:
                if (ids.TryGetValue(target, out string? mapped))
                {
                    result["object_id"] = mapped;
                    return result;
                }
                Drop("block", where, kind);
                return null;
            case "table":
                CleanTable(result);
                return result;
            case "dataview":
                return TemplateDataview(block, result, where);
            default:
                return result;
        }
    }

    // 2026-11-09: a template document says nesting with an `indent` number on
    // each block (a block is the child of the closest earlier block with indent
    // one lower), and the server refuses a document where a `column`s parent is
    // not a `row` (« a column block must be nested directly under a row block »).
    // A source template had a column inside a column. Such a column is removed
    // and each of its descendants moves up one level, repeated until none is
    // left, so its content stays where the layout put it, one level out.
    // Counted as column_unwrapped (kind only).
    internal void UnwrapStrayColumns(JsonArray blocks, string where)
    {
        while (true)
        {
            List<JsonObject> list = blocks.OfType<JsonObject>().ToList();
            int stray = list.FindIndex(0, block =>
                HomeObjectJson.String(block, "type") == "column" && ParentKind(list, list.IndexOf(block)) != "row");
            if (stray < 0) return;

            int level = IndentOf(list[stray]);
            for (int next = stray + 1; next < list.Count && IndentOf(list[next]) > level; next++)
                SetIndent(list[next], IndentOf(list[next]) - 1);
            blocks.Remove(list[stray]);
            Drop("column_unwrapped", where, "column");
        }
    }

    // The kind of the closest earlier block whose indent is one lower, or "".
    private static string ParentKind(List<JsonObject> list, int index)
    {
        int wanted = IndentOf(list[index]) - 1;
        if (wanted < 0) return "";
        for (int earlier = index - 1; earlier >= 0; earlier--)
            if (IndentOf(list[earlier]) == wanted) return HomeObjectJson.String(list[earlier], "type");
        return "";
    }

    private static int IndentOf(JsonObject block) =>
        block["indent"] is JsonValue value && value.TryGetValue(out int indent) ? indent : 0;

    private static void SetIndent(JsonObject block, int indent)
    {
        if (indent <= 0) block.Remove("indent");
        else block["indent"] = indent;
    }

    private static void CleanTable(JsonObject table)
    {
        foreach (JsonObject column in (table["columns"] as JsonArray ?? []).OfType<JsonObject>())
        {
            column.Remove("fields");
            column.Remove("header");
        }
        foreach (JsonObject row in (table["rows"] as JsonArray ?? []).OfType<JsonObject>())
            foreach (JsonNode? cell in row["cells"] as JsonArray ?? [])
            {
                if (cell is JsonObject single) CleanCell(single);
                else if (cell is JsonArray many)
                    foreach (JsonObject inner in many.OfType<JsonObject>()) CleanCell(inner);
            }
    }

    private static void CleanCell(JsonObject cell)
    {
        cell.Remove("id");
        cell.Remove("indent");
        cell.Remove("fields");
        cell.Remove("source");
    }

    // An inline query. Its source is a type (`type-<key>`, remapped), another
    // object (looked up, else the block goes) or this object (no object_id,
    // kept as it is).
    private JsonObject? TemplateDataview(JsonObject block, JsonObject result, string where)
    {
        string? viewType = null;
        string source = HomeObjectJson.String(block, "object_id");
        if (source.StartsWith("type-", StringComparison.Ordinal))
        {
            string typeKey = TypeKey(source["type-".Length..]);
            result["object_id"] = "type-" + typeKey;
            if (HomeSchema.CreatableTypes.Contains(typeKey, StringComparer.Ordinal)) viewType = typeKey;
        }
        else if (source.Length > 0)
        {
            if (!ids.TryGetValue(source, out string? mapped))
            {
                Drop("block", where, "dataview");
                return null;
            }
            result["object_id"] = mapped;
        }

        if (block["properties"] is JsonArray properties)
        {
            var kept = new JsonArray();
            foreach (JsonObject entry in properties.OfType<JsonObject>())
            {
                string property = HomeObjectJson.String(entry, "property");
                if (PropertyKey(property, viewType) is { } key)
                {
                    var copy = (JsonObject)entry.DeepClone();
                    copy["property"] = key;
                    kept.Add(copy);
                }
                else Drop("dataview_property", where, property);
            }
            result["properties"] = kept;
        }

        if (block["views"] is JsonArray views)
            result["views"] = new JsonArray(views.OfType<JsonObject>()
                .Select(view => (JsonNode?)CleanView(view, viewType, where)).ToArray());
        return result;
    }

    // ── Widgets ─────────────────────────────────────────────────────────

    public HomeWidgetPlan? WidgetPlan(HomeStructureWidget widget)
    {
        if (!ids.TryGetValue(widget.Target, out string? target))
        {
            Drop("widget", $"widget {widget.OldId}", widget.Target);
            return null;
        }
        return new HomeWidgetPlan(widget.OldId, target, widget.Layout, widget.Limit, widget.ViewId);
    }

    internal void Drop(string kind, string where, string what)
    {
        Dropped[kind] = Dropped.TryGetValue(kind, out int count) ? count + 1 : 1;
        Details.Add(new HomeStructureDrop(kind, where, what));
    }
}

internal sealed record HomeStructureDrop(string Kind, string Where, string What);

internal sealed record HomeSectionPlan(string Property, string? Section);

internal sealed record HomeViewPlan(
    string OldId, string Name, JsonObject Set, JsonObject Columns, IReadOnlyList<string> Visible);

internal sealed record HomeTypePlan(
    string OldKey,
    string Key,
    string OldTypeId,
    string? DefaultView,
    string? OldDefaultTemplate,
    IReadOnlyList<HomeSectionPlan> Properties,
    IReadOnlyList<HomeViewPlan> Views);

internal sealed record HomeTemplatePlan(string OldId, string Name, string Key, JsonObject Body);

// ViewId is the id of the view written for OldViewId, set by the writer.
internal sealed record HomeWidgetPlan(
    string OldId, string Target, string? Layout, int? Limit, string? OldViewId, string? ViewId = null);
