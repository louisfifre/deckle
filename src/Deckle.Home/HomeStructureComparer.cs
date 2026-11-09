using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Deckle.Anytype;

namespace Deckle.Home;

// Reads the structure back off the target through v2 GETs and holds it against
// what the write settled on (the remapped expectation, HomeStructurePlan):
//
//   types        default_view, default_template, the section of each planned
//                property;
//   type views   the count, then per view its name, type, group_by, filters,
//                sorts and visible columns (as a set: a column patch cannot
//                reorder, so order is not compared);
//   templates    the count, then per template its type, its property values
//                and the histogram of its block kinds;
//   collections  the views, as type views;
//   widgets      the count, the order, and per widget its target, layout and
//                view.
//
// Counts come back; every difference, with the names and values, is written
// beside the sheet only.
internal sealed class HomeStructureComparer(AnytypeApiClient api)
{
    private readonly List<HomeStructureDifference> _differences = [];
    private HomeStructureComparison _result = new();

    public async Task<HomeStructureComparison> CompareAsync(
        HomeStructurePlan plan, string targetSpaceId, string? detailPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSpaceId);
        _result = new HomeStructureComparison();
        _differences.Clear();
        var v2 = new HomeV2(api, targetSpaceId);

        foreach (HomeTypePlan type in plan.Types) await CompareTypeAsync(v2, plan, type, ct).ConfigureAwait(false);
        await CompareTemplatesAsync(v2, plan, ct).ConfigureAwait(false);
        foreach (HomeCollectionPlan collection in plan.Collections)
            await CompareCollectionAsync(v2, plan, collection, ct).ConfigureAwait(false);
        await CompareWidgetsAsync(v2, plan, ct).ConfigureAwait(false);

        if (detailPath is not null)
            HomeReplaySheet.WriteJson(detailPath, new HomeStructureComparisonDetails { Differences = _differences });
        return _result;
    }

    // ── Types ───────────────────────────────────────────────────────────

    private async Task CompareTypeAsync(HomeV2 v2, HomeStructurePlan plan, HomeTypePlan type, CancellationToken ct)
    {
        string what = $"type {type.Key}";
        JsonObject? document;
        try { document = await v2.GetAsync($"types/{type.Key}", ct).ConfigureAwait(false) as JsonObject; }
        catch (HttpRequestException ex)
        {
            Unread("type", what, ex);
            return;
        }
        if (document is null)
        {
            Unread("type", what, new InvalidOperationException("no such type on the target"));
            return;
        }

        JsonObject settings = document["type_settings"] as JsonObject ?? [];
        Check("type.default_view", what, type.DefaultView ?? "", HomeObjectJson.String(settings, "default_view"));
        string expectedTemplate = type.OldDefaultTemplate is not null
            && plan.TemplateIds.TryGetValue(type.OldDefaultTemplate, out string? mapped) ? mapped : "";
        Check("type.default_template", what, expectedTemplate, HomeObjectJson.String(settings, "default_template"));

        JsonArray definitions = settings["property_definitions"] as JsonArray ?? [];
        foreach (HomeSectionPlan section in type.Properties)
        {
            JsonObject? actual = definitions.OfType<JsonObject>()
                .FirstOrDefault(entry => HomeObjectJson.String(entry, "property") == section.Property);
            if (actual is null) continue;
            Check("type.section", $"{what}, {section.Property}", section.Section ?? "", HomeObjectJson.String(actual, "section"));
        }

        CompareViews(
            "type", what, type.Views, HomeV2.DataviewBlock(document)?["views"] as JsonArray ?? [], plan);
    }

    // ── Views ───────────────────────────────────────────────────────────

    private void CompareViews(
        string area, string what, IReadOnlyList<HomeViewPlan> plans, JsonArray actualViews, HomeStructurePlan plan)
    {
        if (plans.Count == 0) return;
        Check($"{area}_view.count", what, plans.Count.ToString(), actualViews.Count.ToString());

        foreach (HomeViewPlan expected in plans)
        {
            string label = $"{what}, view « {expected.Name} »";
            JsonObject? actual = plan.ViewIds.TryGetValue(expected.OldId, out string? id)
                ? actualViews.OfType<JsonObject>().FirstOrDefault(view => HomeObjectJson.Id(view) == id)
                : null;
            if (actual is null)
            {
                Check($"{area}_view.missing", label, "présente", "absente");
                continue;
            }

            Check($"{area}_view.name", label, expected.Name, HomeObjectJson.String(actual, "name"));
            Check($"{area}_view.type", label,
                HomeObjectJson.String(expected.Set, "type") is { Length: > 0 } type ? type : "table",
                HomeObjectJson.String(actual, "type") is { Length: > 0 } shown ? shown : "table");
            Check($"{area}_view.group_by", label,
                HomeObjectJson.String(expected.Set, "group_by"), HomeObjectJson.String(actual, "group_by"));
            Check($"{area}_view.filters", label, FilterText(expected.Set["filters"]), FilterText(actual["filters"]));
            Check($"{area}_view.sorts", label, SortText(expected.Set["sorts"]), SortText(actual["sorts"]));
            Check($"{area}_view.columns", label,
                string.Join(" | ", expected.Visible.Order(StringComparer.Ordinal)),
                string.Join(" | ", VisibleColumns(actual)));
        }
    }

    private static IEnumerable<string> VisibleColumns(JsonObject view) =>
        (view["columns"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(column => !(column["hidden"] is JsonValue flag && flag.TryGetValue(out bool hidden) && hidden))
            .Select(column => HomeObjectJson.String(column, "property"))
            .Where(property => property.Length > 0)
            .Order(StringComparer.Ordinal);

    // Property, condition and value of each leaf; groups by operator; the
    // top level is an implicit AND, so order does not count. A member the
    // server adds (an id, include_time) is not part of the meaning.
    internal static string FilterText(JsonNode? filters)
    {
        string Node(JsonNode? node)
        {
            if (node is not JsonObject value) return "";
            if (value["filters"] is JsonArray nested)
                return $"{HomeObjectJson.String(value, "operator")}({string.Join(", ", nested.Select(Node).Order(StringComparer.Ordinal))})";
            return $"{HomeObjectJson.String(value, "property")} {HomeObjectJson.String(value, "condition")} {ValueText(value["value"])}";
        }
        return string.Join(" ; ", (filters as JsonArray ?? []).Select(Node).Order(StringComparer.Ordinal));
    }

    private static string ValueText(JsonNode? value) => value switch
    {
        null => "",
        JsonArray array => string.Join("|", array.Select(ValueText).Order(StringComparer.Ordinal)),
        JsonValue scalar when scalar.TryGetValue(out string? text) => text ?? "",
        _ => value.ToJsonString(),
    };

    private static string SortText(JsonNode? sorts) =>
        string.Join(" ; ", (sorts as JsonArray ?? []).OfType<JsonObject>().Select(sort =>
            $"{HomeObjectJson.String(sort, "property")}:{(HomeObjectJson.String(sort, "direction") is { Length: > 0 } direction ? direction : "asc")}"));

    // ── Templates ───────────────────────────────────────────────────────

    private async Task CompareTemplatesAsync(HomeV2 v2, HomeStructurePlan plan, CancellationToken ct)
    {
        IReadOnlyList<JsonObject> listed;
        try { listed = await v2.ListAsync("templates", ct).ConfigureAwait(false); }
        catch (HttpRequestException ex)
        {
            Unread("template", "templates", ex);
            return;
        }
        // Per type, the templates written for it.
        foreach (IGrouping<string, HomeTemplatePlan> group in plan.Templates
                     .Where(template => plan.TemplateIds.ContainsKey(template.OldId)).GroupBy(template => template.Key))
            Check("template.count", $"type {group.Key}", group.Count().ToString(),
                group.Count(template => listed.Any(row => HomeObjectJson.Id(row) == plan.TemplateIds[template.OldId])).ToString());

        foreach (HomeTemplatePlan expected in plan.Templates)
        {
            string label = $"template « {expected.Name} »";
            if (!plan.TemplateIds.TryGetValue(expected.OldId, out string? id))
            {
                Check("template.missing", label, "présent", "absent");
                continue;
            }

            JsonObject? actual;
            try { actual = await v2.GetAsync($"objects/{id}", ct).ConfigureAwait(false) as JsonObject; }
            catch (HttpRequestException ex)
            {
                Unread("template", label, ex);
                continue;
            }
            if (actual is null)
            {
                Check("template.missing", label, "présent", "absent");
                continue;
            }

            Check("template.type", label, expected.Key, TemplateFor(actual));
            JsonObject actualProperties = actual["properties"] as JsonObject ?? [];
            foreach ((string key, JsonNode? value) in expected.Body["properties"] as JsonObject ?? [])
                Check("template.property", $"{label}, {key}", ValueText(value),
                    ValueText(actualProperties[key]));
            Check("template.blocks", label,
                Histogram(expected.Body["blocks"] as JsonArray), Histogram(actual["blocks"] as JsonArray));
        }
    }

    private static string TemplateFor(JsonObject document)
    {
        string key = HomeObjectJson.String(document, "template_for");
        return key.StartsWith("type-", StringComparison.Ordinal) ? key["type-".Length..] : key;
    }

    // "dataview 2, heading_2 1": the block kinds of a template, by count.
    internal static string Histogram(JsonArray? blocks) =>
        string.Join(", ", (blocks ?? []).OfType<JsonObject>()
            .GroupBy(block => HomeObjectJson.String(block, "type"))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key} {group.Count()}"));

    // ── Collections ─────────────────────────────────────────────────────

    private async Task CompareCollectionAsync(
        HomeV2 v2, HomeStructurePlan plan, HomeCollectionPlan collection, CancellationToken ct)
    {
        string what = $"collection « {collection.Source.Name} »";
        IReadOnlyList<JsonObject> views;
        try { views = await v2.ListAsync($"collections/{collection.TargetId}/views", ct).ConfigureAwait(false); }
        catch (HttpRequestException ex)
        {
            Unread("collection", what, ex);
            return;
        }
        CompareViews("collection", what, collection.Views, new JsonArray(views.Select(view => (JsonNode?)view).ToArray()), plan);
    }

    // ── Widgets ─────────────────────────────────────────────────────────

    private async Task CompareWidgetsAsync(HomeV2 v2, HomeStructurePlan plan, CancellationToken ct)
    {
        if (plan.Widgets.Count == 0) return;
        IReadOnlyList<JsonObject> listed;
        try { listed = await v2.ListAsync("widgets", ct).ConfigureAwait(false); }
        catch (HttpRequestException ex)
        {
            Unread("widget", "widgets", ex);
            return;
        }
        JsonObject[] actual = listed.Where(widget =>
            HomeObjectJson.String(widget, "scope") is not { Length: > 0 } scope || scope == "space").ToArray();

        // The target may hold widgets of its own: only those on a target the
        // plan names are held against it, in the order the plan wrote them.
        var named = plan.Widgets.Select(widget => widget.Target).ToHashSet(StringComparer.Ordinal);
        JsonObject[] ours = actual.Where(widget => named.Contains(HomeObjectJson.String(widget, "target"))).ToArray();
        Check("widget.count", "widgets", plan.Widgets.Count.ToString(), ours.Length.ToString());

        int cursor = 0;
        foreach (HomeWidgetPlan expected in plan.Widgets)
        {
            string label = $"widget on {expected.Target}";
            int found = Array.FindIndex(ours, cursor, widget => HomeObjectJson.String(widget, "target") == expected.Target);
            if (found < 0)
            {
                int earlier = Array.FindIndex(ours, widget => HomeObjectJson.String(widget, "target") == expected.Target);
                Check(earlier < 0 ? "widget.missing" : "widget.order", label, "à sa place", earlier < 0 ? "absent" : "plus tôt");
                continue;
            }
            cursor = found + 1;
            JsonObject widget = ours[found];
            Check("widget.target", label, expected.Target, HomeObjectJson.String(widget, "target"));
            Check("widget.layout", label, expected.Layout ?? "", HomeObjectJson.String(widget, "layout"));
            Check("widget.view", label, expected.ViewId ?? "", HomeObjectJson.String(widget, "view_id"));
        }
    }

    // ── Shared ──────────────────────────────────────────────────────────

    private void Check(string field, string what, string expected, string actual)
    {
        _result.Compared++;
        if (string.Equals(expected, actual, StringComparison.Ordinal)) return;
        _result.Different++;
        _result.DifferentByField[field] = _result.DifferentByField.TryGetValue(field, out int count) ? count + 1 : 1;
        _differences.Add(new HomeStructureDifference(field, what, expected, actual));
    }

    private void Unread(string area, string what, Exception ex)
    {
        _result.Unread[area] = _result.Unread.TryGetValue(area, out int count) ? count + 1 : 1;
        _differences.Add(new HomeStructureDifference($"{area}.unread", what, "lisible", ex.Message));
    }
}

// Counts of the read-back, safe to print: how many facts were compared, how
// many differ, which fields. Names and values stay in the file.
internal sealed class HomeStructureComparison
{
    public int Compared { get; set; }
    public int Different { get; set; }
    public SortedDictionary<string, int> DifferentByField { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Unread { get; } = new(StringComparer.Ordinal);
}

internal sealed class HomeStructureComparisonDetails
{
    [JsonPropertyName("differences")]
    public List<HomeStructureDifference> Differences { get; set; } = [];
}

internal sealed record HomeStructureDifference(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("what")] string What,
    [property: JsonPropertyName("expected")] string Expected,
    [property: JsonPropertyName("actual")] string Actual);
