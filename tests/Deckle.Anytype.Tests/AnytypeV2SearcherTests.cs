using System.Net;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The v2 search through the same fake backend as the transport. Rows are
// synthetic, shaped like the live answers measured on 2026-10-22.
[Trait("Category", "unit")]
public class AnytypeV2SearcherTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static AnytypeV2Client ClientOver(FakeV2Handler handler) =>
        new(new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"), null, handler);

    static string Page(bool hasMore, int total, params string[] ids) =>
        new JsonObject
        {
            ["data"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject
            {
                ["id"] = id,
                ["name"] = "",
                ["type"] = "rapport",
                ["properties"] = new JsonObject { ["date_du_journal"] = "2026-10-22T00:00:00Z" },
            }).ToArray()),
            ["total"] = total,
            ["offset"] = 0,
            ["limit"] = 2,
            ["has_more"] = hasMore,
        }.ToJsonString();

    [Fact]
    public async Task ASearchSendsItsFiltersSortsAndFieldsInTheBodyAndItsPagingInTheQuery()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, Page(false, 1, "r1"));
        using AnytypeV2Client client = ClientOver(handler);

        await new AnytypeV2Searcher(client).SearchPageAsync(
            new AnytypeV2SearchRequest
            {
                Type = "rapport",
                Filters = [AnytypeV2SearchFilter.In("tache(s)_liee(s)", ["t1", "t2"])],
                Sorts = [new AnytypeV2SearchSort("date_du_journal", Descending: true)],
                Fields = ["date_du_journal"],
            },
            offset: 5, limit: 7, Ct);

        FakeV2Handler.Seen seen = Assert.Single(handler.Requests);
        Assert.Equal("POST", seen.Method);
        Assert.Equal("/v2/spaces/space-1/search?limit=7&offset=5", seen.PathAndQuery);

        JsonObject body = (JsonObject)JsonNode.Parse(seen.Body!)!;
        Assert.Equal("rapport", body["type"]!.GetValue<string>());
        JsonObject filter = (JsonObject)Assert.Single((JsonArray)body["filters"]!)!;
        Assert.Equal("tache(s)_liee(s)", filter["property"]!.GetValue<string>());
        Assert.Equal("in", filter["condition"]!.GetValue<string>());
        Assert.Equal(["t1", "t2"], ((JsonArray)filter["value"]!).Select(n => n!.GetValue<string>()));
        JsonObject sort = (JsonObject)Assert.Single((JsonArray)body["sorts"]!)!;
        Assert.Equal("desc", sort["direction"]!.GetValue<string>());
        Assert.Equal("date_du_journal", ((JsonArray)body["fields"]!).Single()!.GetValue<string>());

        // Paging is the query string's business: the body has no such field.
        Assert.False(body.ContainsKey("limit"));
        Assert.False(body.ContainsKey("offset"));
        // The compact form is the other half of an either/or: never both.
        Assert.False(body.ContainsKey("filter"));
        Assert.False(body.ContainsKey("query"));
    }

    [Fact]
    public async Task ARowKeepsItsIdNameTypeAndTypedProperties()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, Page(false, 1, "r1"));
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2SearchPage page = await new AnytypeV2Searcher(client)
            .SearchPageAsync(new AnytypeV2SearchRequest { Type = "rapport" }, 0, 10, Ct);

        AnytypeV2SearchRow row = Assert.Single(page.Rows);
        Assert.Equal("r1", row.Id);
        Assert.Equal("", row.Name);
        Assert.Equal("rapport", row.Type);
        Assert.Equal("2026-10-22", row.Properties.Day("date_du_journal"));
        Assert.Equal(1, page.Total);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task SearchAllFollowsHasMoreUntilTheLastPage()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, Page(true, 3, "r1", "r2"))
            .Reply(HttpStatusCode.OK, Page(false, 3, "r3"));
        using AnytypeV2Client client = ClientOver(handler);

        IReadOnlyList<AnytypeV2SearchRow> rows = await new AnytypeV2Searcher(client)
            .SearchAllAsync(new AnytypeV2SearchRequest { Type = "rapport" }, Ct);

        Assert.Equal(["r1", "r2", "r3"], rows.Select(r => r.Id));
        Assert.Equal(2, handler.Requests.Count);
        // The second page starts where the first one stopped.
        Assert.Contains("offset=0", handler.Requests[0].PathAndQuery);
        Assert.Contains("offset=2", handler.Requests[1].PathAndQuery);
        Assert.Contains($"limit={AnytypeV2Searcher.PageSize}", handler.Requests[1].PathAndQuery);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task SearchAllStopsOnAnEmptyPageEvenIfTheServerStillClaimsMore()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, Page(true, 9, "r1"))
            .Reply(HttpStatusCode.OK, Page(true, 9));
        using AnytypeV2Client client = ClientOver(handler);

        IReadOnlyList<AnytypeV2SearchRow> rows = await new AnytypeV2Searcher(client)
            .SearchAllAsync(new AnytypeV2SearchRequest(), Ct);

        Assert.Equal(["r1"], rows.Select(r => r.Id));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task SearchAllAdvancesByTheRowsTheServerSentNotTheOnesItKept()
    {
        // The first page carries a row with no id: it is dropped, but the
        // server still counted it, so the next offset is 2, not 1.
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, Page(true, 3, "r1", ""))
            .Reply(HttpStatusCode.OK, Page(false, 3, "r3"));
        using AnytypeV2Client client = ClientOver(handler);

        IReadOnlyList<AnytypeV2SearchRow> rows = await new AnytypeV2Searcher(client)
            .SearchAllAsync(new AnytypeV2SearchRequest(), Ct);

        Assert.Equal(["r1", "r3"], rows.Select(r => r.Id));
        Assert.Contains("offset=2", handler.Requests[1].PathAndQuery);
    }

    // A search changes nothing, so a 5xx may be retried like a GET even though
    // it travels on a POST with no idempotency key.
    [Fact]
    public async Task ASearchIsResentAfterAServerError()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.ServiceUnavailable, """{"error":"busy"}""", ("Retry-After", "0"))
            .Reply(HttpStatusCode.OK, Page(false, 1, "r1"));
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2SearchPage page = await new AnytypeV2Searcher(client)
            .SearchPageAsync(new AnytypeV2SearchRequest(), 0, 10, Ct);

        Assert.Single(page.Rows);
        Assert.Equal(2, handler.Requests.Count);
        Assert.False(handler.Requests[0].HasHeader("Idempotency-Key"));
    }
}
