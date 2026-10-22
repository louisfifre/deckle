using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The exhaustive reads walk a list page by page. The server decides how many
// rows a page holds (a ceiling the client cannot see), so the next offset is
// the rows it sent back, never the limit the client asked for: advancing by the
// limit skips every row between the two. FakeAnytypeServer's paged lists serve
// from the query's offset under such a ceiling.
[Trait("Category", "integration")]
public class ListingPagingTests
{
    // Smaller than every limit the readers ask for, so each of them is capped.
    const int ServerPageCap = 40;
    const int Rows = 130;

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A run that cannot end must fail the test, not hang the suite.
    static CancellationToken Bounded()
    {
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return CancellationTokenSource.CreateLinkedTokenSource(Ct, timeout.Token).Token;
    }

    static IEnumerable<JsonObject> Objects(int count) =>
        Enumerable.Range(0, count).Select(i => new JsonObject
        {
            ["object"] = "object",
            ["id"] = $"object-{i:D4}",
            ["name"] = $"Collection {i}",
        });

    [Fact]
    [Trait("Category", "regression")]
    public async Task AnObjectListingReadsEveryRowWhenTheServerCapsThePage()
    {
        using var server = new FakeAnytypeServer();
        server.OnPagedSearch(Objects(Rows), ServerPageCap);

        IReadOnlyList<JsonObject> listed = await ObjectListing.AllOfTypesAsync(
            new AnytypeApiClient(server.Credentials), FakeAnytypeServer.Space, ["collection"], Ct);

        Assert.Equal(
            Objects(Rows).Select(o => o["id"]!.GetValue<string>()),
            listed.Select(o => o["id"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AnObjectListingStopsOnAnEmptyPageEvenIfTheServerStillClaimsMore()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray(Objects(3).Cast<JsonNode>().ToArray()),
            ["pagination"] = new JsonObject { ["has_more"] = true },
        });
        // The fake repeats its last answer, so every later request meets this
        // empty page that still claims more.
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray(),
            ["pagination"] = new JsonObject { ["has_more"] = true },
        });

        IReadOnlyList<JsonObject> listed = await ObjectListing.AllOfTypesAsync(
            new AnytypeApiClient(server.Credentials), FakeAnytypeServer.Space, ["collection"], Bounded());

        Assert.Equal(3, listed.Count);
        Assert.Equal(2, server.Requests.Count);
    }
}
