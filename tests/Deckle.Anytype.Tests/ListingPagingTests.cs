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

    static IEnumerable<JsonObject> Types(int count) =>
        Enumerable.Range(0, count).Select(i => new JsonObject
        {
            ["object"] = "type",
            ["id"] = $"type-id-{i:D4}",
            ["key"] = $"type_{i:D4}",
            ["name"] = $"Type {i}",
            ["plural_name"] = $"Types {i}",
            ["layout"] = "basic",
        });

    static IEnumerable<JsonObject> Properties(int count) =>
        Enumerable.Range(0, count).Select(i => new JsonObject
        {
            ["object"] = "property",
            ["id"] = $"prop-id-{i:D4}",
            ["key"] = $"prop_{i:D4}",
            ["name"] = $"Property {i}",
            ["format"] = "select",
        });

    static IEnumerable<JsonObject> Tags(int count) =>
        Enumerable.Range(0, count).Select(i => new JsonObject
        {
            ["object"] = "tag",
            ["id"] = $"tag-id-{i:D4}",
            ["key"] = $"option_{i:D4}",
            ["name"] = $"Option {i}",
            ["color"] = "grey",
        });

    static JsonObject EmptyPageClaimingMore() => new()
    {
        ["data"] = new JsonArray(),
        ["pagination"] = new JsonObject { ["has_more"] = true },
    };

    static JsonObject EmptyList() => new()
    {
        ["data"] = new JsonArray(),
        ["pagination"] = new JsonObject { ["has_more"] = false },
    };

    static SchemaSnapshotReader NewReader(FakeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials));

    [Fact]
    [Trait("Category", "regression")]
    public async Task ASchemaSnapshotReadsEveryTypeWhenTheServerCapsThePage()
    {
        using var server = new FakeAnytypeServer();
        server.OnPagedListTypes(Types(Rows), ServerPageCap);
        server.OnListProperties(EmptyList());

        SchemaSnapshot snapshot = await NewReader(server).ReadAsync(FakeAnytypeServer.Space, ct: Ct);

        Assert.Equal(Rows, snapshot.Types.Count);
        Assert.Contains("type_0090", snapshot.Types.Keys);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ASchemaSnapshotReadsEveryPropertyWhenTheServerCapsThePage()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnPagedListProperties(Properties(Rows), ServerPageCap);

        SchemaSnapshot snapshot = await NewReader(server).ReadAsync(FakeAnytypeServer.Space, ct: Ct);

        Assert.Equal(Rows, snapshot.Properties.Count);
        Assert.Contains("prop_0090", snapshot.Properties.Keys);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ASchemaSnapshotReadsEveryTagWhenTheServerCapsThePage()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnPagedListProperties(Properties(3), ServerPageCap);
        server.OnPagedListPropertyTags("prop-id-0001", Tags(Rows), ServerPageCap);

        SchemaSnapshot snapshot = await NewReader(server)
            .ReadAsync(FakeAnytypeServer.Space, ["prop_0001"], Ct);

        IReadOnlyDictionary<string, SchemaTagInfo> tags = snapshot.TagsByProperty["prop_0001"];
        Assert.Equal(Enumerable.Range(0, Rows).Select(i => $"option_{i:D4}"), tags.Keys.Where(k => k.StartsWith("option_")).Order());
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ASchemaSnapshotStopsOnAnEmptyPageEvenIfTheServerStillClaimsMore()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyPageClaimingMore());
        server.OnListProperties(EmptyList());

        SchemaSnapshot snapshot = await NewReader(server)
            .ReadAsync(FakeAnytypeServer.Space, ct: Bounded());

        Assert.Empty(snapshot.Types);
        Assert.Single(server.Requests, r => r.Path.EndsWith("/types"));
    }

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
