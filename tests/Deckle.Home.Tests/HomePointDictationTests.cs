using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// A room is dictated, not proposed: the codes are minted as the lowest free
// number of each category in the room, gaps reused, counting the batch, and
// the points are written at once.
[Trait("Category", "integration")]
public sealed class HomePointDictationTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    static FakeHomeAnytypeServer ServerWithARoomAndAGap()
    {
        var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-PS01", "Prise fictive un", "room-zz"),
            FakeHomeAnytypeServer.Point("point-3", "ZZ-PS03", "Prise fictive trois", "room-zz"));
        return server;
    }

    static string[] CreatedCodes(FakeHomeAnytypeServer server) => server.Requests
        .Where(request => request.Method == "POST" && !request.Path.Contains("/lists/", StringComparison.Ordinal))
        .Select(request => JsonNode.Parse(request.Body)!["properties"]!.AsArray().OfType<JsonObject>()
            .Single(entry => entry["key"]!.GetValue<string>() == HomeSchema.Properties.Code)["text"]!.GetValue<string>())
        .ToArray();

    static ToolDescriptor Tool(FakeHomeAnytypeServer server, string name) =>
        HomeToolCatalog.Build(() => Gestures(server)).Single(tool => tool.Name == name);

    [Fact]
    public async Task DictationMintsTheLowestFreeCodesAndWritesAtOnce()
    {
        using FakeHomeAnytypeServer server = ServerWithARoomAndAGap();

        string digest = await Gestures(server).DictatePointsAsync(
            "ZZ",
            [
                new HomeCreateItem(null, "Prise fictive a", null, Category: "PS"),
                new HomeCreateItem(null, "Prise fictive b", null, Category: "ps"),
                new HomeCreateItem(null, "Plafonnier fictif", null, Category: "LC — plafonnier"),
            ],
            Ct);

        Assert.Equal(new[] { "ZZ-PS02", "ZZ-PS04", "ZZ-LC01" }, CreatedCodes(server));
        Assert.StartsWith("Dicté dans ZZ · Pièce fictive :", digest);
        Assert.Contains("- ZZ-PS02 · Prise fictive a (id created-1)", digest);
        Assert.Contains("- ZZ-LC01 · Plafonnier fictif (id created-3)", digest);
    }

    [Fact]
    public async Task ADictatedPointIsInstalledInItsRoomWithItsCategory()
    {
        using FakeHomeAnytypeServer server = ServerWithARoomAndAGap();

        await Gestures(server).DictatePointsAsync(
            "Pièce fictive", [new HomeCreateItem(null, "Interrupteur fictif", null, Category: "CS")], Ct);

        JsonArray properties = JsonNode.Parse(server.Requests.Single(request => request.Method == "POST").Body)!
            ["properties"]!.AsArray();
        Assert.Equal("room-zz", properties.OfType<JsonObject>()
            .Single(entry => entry["key"]!.GetValue<string>() == HomeSchema.Properties.InstalledIn)["objects"]![0]!.GetValue<string>());
        Assert.Equal("tag-category-cs", properties.OfType<JsonObject>()
            .Single(entry => entry["key"]!.GetValue<string>() == HomeSchema.Properties.Category)["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnUnknownRoomStopsTheDictationBeforeAnyWrite()
    {
        using FakeHomeAnytypeServer server = ServerWithARoomAndAGap();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Gestures(server).DictatePointsAsync(
            "Pièce absente fictive", [new HomeCreateItem(null, "Prise fictive", null, Category: "PS")], Ct));

        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task ThePointDictateToolRefusesACodeBeforeHomeIsRead()
    {
        ToolDescriptor tool = HomeToolCatalog.Build(() => throw new InvalidOperationException("must not resolve Home"))
            .Single(value => value.Name == "point_dictate");

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => tool.Handler(new JsonObject
        {
            ["room"] = "ZZ",
            ["points"] = new JsonArray(new JsonObject { ["category"] = "PS", ["name"] = "Prise fictive", ["code"] = "ZZ-PS09" }),
        }, Ct));

        Assert.Contains("points[0]", error.Message);
        Assert.Contains("Unknown field 'code'", error.Message);
    }

    [Fact]
    public async Task PointCreateMintsWhenGivenACategoryAndARoom()
    {
        using FakeHomeAnytypeServer server = ServerWithARoomAndAGap();

        await Tool(server, "point_create").Handler(new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["category"] = "PS", ["room"] = "ZZ", ["name"] = "Prise fictive deux",
            }),
        }, Ct);

        Assert.Equal(new[] { "ZZ-PS02" }, CreatedCodes(server));
    }

    [Fact]
    public async Task AMintedCodeNeverTakesACodeALaterItemNames()
    {
        using FakeHomeAnytypeServer server = ServerWithARoomAndAGap();

        await Tool(server, "point_create").Handler(new JsonObject
        {
            ["items"] = new JsonArray(
                new JsonObject { ["category"] = "PS", ["room"] = "ZZ", ["name"] = "Prise fictive minted" },
                new JsonObject { ["code"] = "ZZ-PS02", ["name"] = "Prise fictive given" }),
        }, Ct);

        Assert.Equal(new[] { "ZZ-PS04", "ZZ-PS02" }, CreatedCodes(server));
    }

    [Fact]
    public async Task APointGivenBothACodeAndACategoryIsRefused()
    {
        ToolDescriptor tool = HomeToolCatalog.Build(() => throw new InvalidOperationException("must not resolve Home"))
            .Single(value => value.Name == "point_create");

        await Assert.ThrowsAsync<ArgumentException>(() => tool.Handler(new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject
            {
                ["code"] = "ZZ-PS02", ["category"] = "PS", ["room"] = "ZZ", ["name"] = "Prise fictive",
            }),
        }, Ct));
    }
}
