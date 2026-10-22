using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// One structured filter node of a search: a property key, a condition and its
// value. Measured to be the only form that accepts the report's link key
// (`tache(s)_liee(s)`: its parentheses make the compact filter string answer
// 400). Condition is the API's own word (equal, in, not_in, contains, empty,
// not_empty…); a select takes option names and a relation takes ids.
public sealed record AnytypeV2SearchFilter(string Property, string Condition, JsonNode? Value = null)
{
    public static AnytypeV2SearchFilter Equal(string property, string value) =>
        new(property, "equal", JsonValue.Create(value));

    public static AnytypeV2SearchFilter In(string property, IEnumerable<string> values) =>
        new(property, "in", new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()));

    internal JsonObject ToJson()
    {
        var node = new JsonObject { ["property"] = Property, ["condition"] = Condition };
        if (Value is not null) node["value"] = Value.DeepClone();
        return node;
    }
}

public sealed record AnytypeV2SearchSort(string Property, bool Descending = false)
{
    internal JsonObject ToJson() => new()
    {
        ["property"] = Property,
        ["direction"] = Descending ? "desc" : "asc",
    };
}

// What a search asks. Type is one type key (the API takes a single one); Query
// is the full-text part over names and indexed content; Filters are combined
// with an implicit AND; Fields lists the property keys each row should carry,
// since a row has none beyond its name unless asked. Paging is not part of the
// request: it rides in the query string, see AnytypeV2Searcher.
public sealed record AnytypeV2SearchRequest
{
    public string? Type { get; init; }

    public string? Query { get; init; }

    public IReadOnlyList<AnytypeV2SearchFilter>? Filters { get; init; }

    public IReadOnlyList<AnytypeV2SearchSort>? Sorts { get; init; }

    public IReadOnlyList<string>? Fields { get; init; }

    internal JsonObject ToBody()
    {
        var body = new JsonObject();
        if (!string.IsNullOrEmpty(Type)) body["type"] = Type;
        if (!string.IsNullOrEmpty(Query)) body["query"] = Query;
        // Always the structured form: the compact `filter` string is the other
        // half of an either/or, and the one that cannot spell every key.
        if (Filters is { Count: > 0 })
            body["filters"] = new JsonArray(Filters.Select(f => (JsonNode)f.ToJson()).ToArray());
        if (Sorts is { Count: > 0 })
            body["sorts"] = new JsonArray(Sorts.Select(s => (JsonNode)s.ToJson()).ToArray());
        if (Fields is { Count: > 0 })
            body["fields"] = new JsonArray(Fields.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray());
        return body;
    }
}

// One row of a search answer: the object's id, name and type key, and the
// properties the request's Fields asked for (a report's name is empty).
public sealed record AnytypeV2SearchRow(string Id, string Name, string Type, AnytypeV2PropertyMap Properties);

// Returned counts what the server sent, including rows skipped for lack of an
// id: the next offset advances by it, not by Rows.Count.
public sealed record AnytypeV2SearchPage(IReadOnlyList<AnytypeV2SearchRow> Rows, int Total, bool HasMore, int Returned);

// POST /v2/spaces/{id}/search. A read carried by POST: the filtering is done by
// the server, which is the point, and the page is chosen in the query string
// (`limit`, `offset`), never in the body. Measured live on 2026-10-22: rows
// carry no snippet and no markdown, and objects in Anytype's bin are left out
// unless a filter asks for `archived = true` (none of 54 binned objects showed
// in a listing by type or a full-text search on its exact name), so no caller
// filters the bin. That filter key is refused (400) once a type is named.
public sealed class AnytypeV2Searcher(AnytypeV2Client client)
{
    // The v1 search served up to 1000 per page. The v2 ceiling was not
    // measured (the default is 25), so a moderate page is asked and the loop
    // follows has_more, which is correct whatever the ceiling is.
    public const int PageSize = 100;

    private readonly AnytypeV2Client _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<AnytypeV2SearchPage> SearchPageAsync(
        AnytypeV2SearchRequest request, int offset, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        AnytypeV2Response response = await _client.SendAsync(
                new AnytypeV2Request(
                    HttpMethod.Post,
                    "search",
                    request.ToBody(),
                    Query:
                    [
                        new("limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new("offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ],
                    ReadOnly: true),
                ct)
            .ConfigureAwait(false);

        if (response.Body is not JsonObject body)
            throw new HttpRequestException("Anytype API returned a non-object JSON root for a search.");

        var rows = new List<AnytypeV2SearchRow>();
        int returned = 0;
        foreach (JsonNode? node in body["data"] as JsonArray ?? [])
        {
            returned++;
            if (node is not JsonObject row) continue;
            string id = row["id"]?.GetValue<string>() ?? "";
            if (id.Length == 0) continue;
            rows.Add(new AnytypeV2SearchRow(
                id,
                row["name"]?.GetValue<string>() ?? "",
                row["type"]?.GetValue<string>() ?? "",
                new AnytypeV2PropertyMap(row["properties"] as JsonObject)));
        }

        return new AnytypeV2SearchPage(
            rows,
            body["total"]?.GetValue<int>() ?? rows.Count,
            body["has_more"]?.GetValue<bool>() == true,
            returned);
    }

    // Every row of the request, one page after the other until has_more turns
    // false. An empty page ends the loop even if has_more were still true, so a
    // misbehaving server cannot hold it forever.
    public async Task<IReadOnlyList<AnytypeV2SearchRow>> SearchAllAsync(
        AnytypeV2SearchRequest request, CancellationToken ct = default)
    {
        var rows = new List<AnytypeV2SearchRow>();
        int offset = 0;
        while (true)
        {
            AnytypeV2SearchPage page = await SearchPageAsync(request, offset, PageSize, ct).ConfigureAwait(false);
            rows.AddRange(page.Rows);
            if (!page.HasMore || page.Returned == 0) break;
            offset += page.Returned;
        }
        return rows;
    }
}
