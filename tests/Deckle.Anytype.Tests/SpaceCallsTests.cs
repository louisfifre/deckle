using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The space-level calls a maintenance rehearsal uses: list the spaces the bot
// reaches, create a TEST space. Driven through FakeAnytypeServer.
[Trait("Category", "integration")]
public class SpaceCallsTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A walk that cannot end must fail the test, not hang the suite.
    static CancellationToken Bounded()
    {
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return CancellationTokenSource.CreateLinkedTokenSource(Ct, timeout.Token).Token;
    }

    static JsonObject Space(int i) => new()
    {
        ["object"] = "anytype.space",
        ["id"] = $"space-{i}",
        ["name"] = $"Space {i}",
    };

    [Fact]
    public async Task ListingSpacesWalksEveryPageTheServerCaps()
    {
        using var server = new FakeAnytypeServer();
        server.OnPagedListSpaces(Enumerable.Range(0, 3).Select(Space), pageCap: 2);
        using var api = new AnytypeApiClient(server.Credentials);

        IReadOnlyList<AnytypeSpaceSummary> spaces = await api.ListSpacesAsync(Bounded());

        Assert.Equal(
            [new("space-0", "Space 0"), new("space-1", "Space 1"), new AnytypeSpaceSummary("space-2", "Space 2")],
            spaces);
        Assert.Equal(["?offset=0&limit=1000", "?offset=2&limit=1000"], server.Requests.Select(r => r.Query));
    }

    [Fact]
    public async Task ListingSpacesStopsOnAnEmptyPageEvenIfTheServerStillClaimsMore()
    {
        using var server = new FakeAnytypeServer();
        server.OnListSpaces(new JsonObject
        {
            ["data"] = new JsonArray(Space(0), Space(1)),
            ["pagination"] = new JsonObject { ["has_more"] = true },
        });
        // The fake repeats its last answer: every later request meets this.
        server.OnListSpaces(new JsonObject
        {
            ["data"] = new JsonArray(),
            ["pagination"] = new JsonObject { ["has_more"] = true },
        });
        using var api = new AnytypeApiClient(server.Credentials);

        IReadOnlyList<AnytypeSpaceSummary> spaces = await api.ListSpacesAsync(Bounded());

        Assert.Equal(2, spaces.Count);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task CreatingASpaceSendsNameAndDescriptionAndReadsTheIdBack()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostSpace(new JsonObject
        {
            ["space"] = new JsonObject
            {
                ["object"] = "anytype.space",
                ["id"] = "created-space",
                ["name"] = "Home TEST",
                ["description"] = "Rehearsal",
            },
        });
        using var api = new AnytypeApiClient(server.Credentials);

        AnytypeSpaceSummary created = await api.CreateSpaceAsync("Home TEST", "Rehearsal", Ct);

        Assert.Equal(new AnytypeSpaceSummary("created-space", "Home TEST"), created);
        FakeAnytypeServer.Received request = Assert.Single(server.Requests);
        Assert.Equal("/v1/spaces", request.Path);
        JsonObject body = server.LastBodyFor("POST");
        Assert.Equal("Home TEST", body["name"]!.GetValue<string>());
        Assert.Equal("Rehearsal", body["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreatingASpaceThrowsOnARefusal()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostSpace(new JsonObject { ["message"] = "bad request" }, status: 400);
        using var api = new AnytypeApiClient(server.Credentials);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => api.CreateSpaceAsync("Home TEST", null, Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatingASpaceRefusesABlankNameBeforeAnyRequest(string name)
    {
        using var server = new FakeAnytypeServer();
        using var api = new AnytypeApiClient(server.Credentials);

        await Assert.ThrowsAsync<ArgumentException>(() => api.CreateSpaceAsync(name, "x", Ct));

        Assert.Empty(server.Requests);
    }
}
