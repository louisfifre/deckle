using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// One column of a view, in the view's order. Hidden columns stay in the list:
// the app keeps their place for when they are shown again.
public sealed record SchemaViewColumn(string Property, bool Hidden);

// One group of a grouped view (kanban, list grouped by…). Id is the option id
// for a select, "empty" for the no-value group, or the API's own word for other
// formats; Color is the group's background, null when the app's default.
public sealed record SchemaViewGroup(string Id, string? Color, bool Hidden);

// One stored view, as /v2 serves it. Filters and Sorts are already put in
// words: they are read, never written back, so the JSON is not kept. Settings
// are the remaining scalar fields (card_size, hide_icon, wrap_content…), named
// as the API names them, so a field the API adds later still shows up.
public sealed record SchemaViewInfo(
    string Name,
    string Layout,
    string? GroupBy,
    IReadOnlyList<string> Filters,
    IReadOnlyList<string> Sorts,
    IReadOnlyList<SchemaViewColumn> Columns,
    IReadOnlyList<SchemaViewGroup> Groups,
    IReadOnlyList<KeyValuePair<string, string>> Settings,
    int ManualOrderGroups);

// The views of one dataview, or why they could not be read. A failure is a
// value, not an exception: one unreadable type must not hide the others.
public sealed record SchemaViewsRead(IReadOnlyList<SchemaViewInfo> Views, string? Failure, bool Denied = false)
{
    internal static SchemaViewsRead Failed(AnytypeV2Exception ex) => new(
        [],
        ex.Code is { Length: > 0 } code
            ? string.Create(CultureInfo.InvariantCulture, $"HTTP {ex.Status} {code}")
            : string.Create(CultureInfo.InvariantCulture, $"HTTP {ex.Status}"),
        ex.Status is 401 or 403);
}

// A set or collection object and its views. Kind is the type key (query or
// collection).
public sealed record SchemaViewSource(string Id, string Name, string Kind, SchemaViewsRead Views);

public sealed record SchemaSetsRead(IReadOnlyList<SchemaViewSource> Sources, string? Failure);

// Reads the stored views of a space through /v2, the only version that serves
// them. A type's views ride in its object_type document (GET /types/{key}, one
// dataview block); a query's or a collection's come from their own /views
// route. Read only: the four v2 view ops stay unmodeled (removed in 9ab75d60).
internal sealed class SchemaViewReader(AnytypeV2Client client)
{
    private const int PageLimit = 100;

    // The view fields rendered on their own line, or not at all (id).
    private static readonly HashSet<string> StructuredFields = new(StringComparer.Ordinal)
    {
        "id", "name", "type", "group_by", "filters", "filter", "sorts", "columns",
        "groups", "object_orders",
    };

    public async Task<SchemaViewsRead> ReadTypeViewsAsync(string spaceId, string typeKey, CancellationToken ct)
    {
        try
        {
            AnytypeV2Response response = await client.SendAsync(
                    new AnytypeV2Request(
                        HttpMethod.Get, $"types/{Uri.EscapeDataString(typeKey)}", SpaceId: spaceId),
                    ct)
                .ConfigureAwait(false);

            JsonObject? dataview = ((response.Body as JsonObject)?["blocks"] as JsonArray)?
                .OfType<JsonObject>()
                .FirstOrDefault(block => SchemaApiJson.Str(block, "type") == "dataview");
            return new SchemaViewsRead(ParseViews(dataview?["views"] as JsonArray), null);
        }
        catch (AnytypeV2Exception ex)
        {
            return SchemaViewsRead.Failed(ex);
        }
    }

    // Every query (set) and collection of the space, each with its views. The
    // row's type is re-checked: a search by type is the server's filter, and a
    // collection-LAYOUT domain type must not pass for a collection.
    public async Task<SchemaSetsRead> ReadSetsAsync(string spaceId, CancellationToken ct)
    {
        var searcher = new AnytypeV2Searcher(client, spaceId);
        var sources = new List<SchemaViewSource>();
        try
        {
            foreach ((string kind, string route) in new[] { ("query", "queries"), ("collection", "collections") })
            {
                IReadOnlyList<AnytypeV2SearchRow> rows = await searcher
                    .SearchAllAsync(new AnytypeV2SearchRequest { Type = kind }, ct)
                    .ConfigureAwait(false);
                foreach (AnytypeV2SearchRow row in rows.Where(r => r.Type == kind))
                    sources.Add(new SchemaViewSource(
                        row.Id, row.Name, kind, await ReadListViewsAsync(spaceId, route, row.Id, ct).ConfigureAwait(false)));
            }
        }
        catch (AnytypeV2Exception ex)
        {
            return new SchemaSetsRead(sources, SchemaViewsRead.Failed(ex).Failure);
        }
        return new SchemaSetsRead(sources, null);
    }

    private async Task<SchemaViewsRead> ReadListViewsAsync(
        string spaceId, string route, string id, CancellationToken ct)
    {
        var views = new JsonArray();
        try
        {
            for (int offset = 0; ;)
            {
                AnytypeV2Response response = await client.SendAsync(
                        new AnytypeV2Request(
                            HttpMethod.Get,
                            $"{route}/{id}/views",
                            Query:
                            [
                                new("offset", offset.ToString(CultureInfo.InvariantCulture)),
                                new("limit", PageLimit.ToString(CultureInfo.InvariantCulture)),
                            ],
                            SpaceId: spaceId),
                        ct)
                    .ConfigureAwait(false);

                JsonObject? body = response.Body as JsonObject;
                JsonArray page = body?["data"] as JsonArray ?? [];
                foreach (JsonNode? view in page) views.Add(view?.DeepClone());
                // An empty page ends the loop even if has_more says otherwise.
                if (body?["has_more"]?.GetValue<bool>() != true || page.Count == 0) break;
                offset += page.Count;
            }
        }
        catch (AnytypeV2Exception ex)
        {
            return SchemaViewsRead.Failed(ex);
        }
        return new SchemaViewsRead(ParseViews(views), null);
    }

    internal static IReadOnlyList<SchemaViewInfo> ParseViews(JsonArray? views)
    {
        if (views is null) return [];
        var result = new List<SchemaViewInfo>();
        foreach (JsonObject view in views.OfType<JsonObject>())
            result.Add(ParseView(view));
        return result;
    }

    private static SchemaViewInfo ParseView(JsonObject view)
    {
        var settings = new List<KeyValuePair<string, string>>();
        foreach ((string key, JsonNode? value) in view)
        {
            if (StructuredFields.Contains(key) || value is null) continue;
            settings.Add(new(key, value is JsonValue scalar ? Scalar(scalar) : value.ToJsonString()));
        }

        string? groupBy = SchemaApiJson.Str(view, "group_by") is { Length: > 0 } g ? g : null;
        return new SchemaViewInfo(
            SchemaApiJson.Str(view, "name"),
            // The API omits a field at its zero value, and the zero view type
            // is the table (survey 2026-10-21: views with no type are tables in
            // the app). An interpretation, stated in the gesture's footer.
            SchemaApiJson.Str(view, "type") is { Length: > 0 } layout ? layout : "table",
            groupBy,
            Filters(view),
            (view["sorts"] as JsonArray ?? []).OfType<JsonObject>().Select(Sort).ToArray(),
            (view["columns"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(c => new SchemaViewColumn(SchemaApiJson.Str(c, "property"), Bool(c, "hidden")))
                .Where(c => c.Property.Length > 0)
                .ToArray(),
            (view["groups"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(gr => new SchemaViewGroup(
                    SchemaApiJson.Id(gr),
                    SchemaApiJson.Str(gr, "background_color") is { Length: > 0 } color ? color : null,
                    Bool(gr, "hidden")))
                .ToArray(),
            settings,
            (view["object_orders"] as JsonArray)?.Count ?? 0);
    }

    // The structured filters, one top-level node per entry; the app combines
    // them with AND. A view may also carry the compact `filter` string, which
    // is shown as served.
    private static IReadOnlyList<string> Filters(JsonObject view)
    {
        var result = (view["filters"] as JsonArray ?? []).OfType<JsonObject>().Select(Filter).ToList();
        if (SchemaApiJson.Str(view, "filter") is { Length: > 0 } compact) result.Add(compact);
        return result;
    }

    internal static string Filter(JsonObject node)
    {
        if (node["filters"] is JsonArray inner)
        {
            string op = SchemaApiJson.Str(node, "operator") is { Length: > 0 } o ? o : "and";
            return "(" + string.Join($" {op} ", inner.OfType<JsonObject>().Select(Filter)) + ")";
        }

        string text = $"{SchemaApiJson.Str(node, "property")} {SchemaApiJson.Str(node, "condition")}";
        if (node["value"] is JsonNode value) text += " " + Value(value);
        if (SchemaApiJson.Str(node, "date_preset") is { Length: > 0 } preset) text += $" ({preset})";
        return text;
    }

    // A sort with no direction is ascending: the API omits the zero value.
    internal static string Sort(JsonObject sort)
    {
        string direction = SchemaApiJson.Str(sort, "direction") is { Length: > 0 } d ? d : "asc";
        string text = $"{SchemaApiJson.Str(sort, "property")} {direction}";
        if (sort["custom_order"] is JsonArray custom)
            text += " [" + string.Join(", ", custom.Select(v => v is null ? "" : Value(v))) + "]";
        string empty = SchemaApiJson.Str(sort, "empty_placement");
        if (empty == "end") text += ", vides en fin";
        else if (empty == "start") text += ", vides en tête";
        return text;
    }

    private static string Value(JsonNode value) => value switch
    {
        JsonArray array => "[" + string.Join(", ", array.Select(v => v is null ? "null" : Value(v))) + "]",
        JsonValue scalar => Scalar(scalar),
        _ => value.ToJsonString(),
    };

    private static string Scalar(JsonValue value) =>
        value.TryGetValue<string>(out string? s) ? s
        : value.TryGetValue<bool>(out bool b) ? (b ? "true" : "false")
        : value.ToJsonString();

    private static bool Bool(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue<bool>(out bool b) && b;
}
