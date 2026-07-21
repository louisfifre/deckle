using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "integration")]
public class HomeGesturesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    [Fact]
    public async Task CreateDerivesRoomCategoryExistenceAndElementTitle()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        string digest = await Gestures(server).CreateAsync(
            HomeSchema.Types.Outlet,
            [new HomeCreateItem("ZZ-P01", null, new JsonObject { ["Libellé"] = "Prise témoin" })],
            Ct);

        Assert.Contains("ZZ-P01", digest);
        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Outlet, body["type_key"]!.GetValue<string>());
        Assert.Equal("ZZ-P01", body["name"]!.GetValue<string>());
        JsonArray properties = Assert.IsType<JsonArray>(body["properties"]);
        Assert.DoesNotContain(properties.OfType<JsonObject>(), value =>
            value["key"]?.GetValue<string>() == "code");
        JsonNode roomReference = Assert.Single(Assert.IsType<JsonArray>(
            Entry(properties, HomeSchema.Properties.Room)["objects"]))!;
        Assert.Equal("room-zz", roomReference.GetValue<string>());
        Assert.Equal("tag-categorie-p", Entry(properties, HomeSchema.Properties.Category)["select"]!.GetValue<string>());
        Assert.Equal("tag-existence-existant", Entry(properties, HomeSchema.Properties.Existence)["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateAddsTheCreatedObjectToCollectionsOutsideItsProperties()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Collection("floor-1", "Niveau fictif"));

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Room,
            [new HomeCreateItem("ZZ", "Pièce fictive", null, ["Niveau fictif"])],
            Ct);

        FakeHomeAnytypeServer.Received membership = server.Requests.Single(request =>
            request.Method == "POST" && request.Path.EndsWith("/lists/floor-1/objects", StringComparison.Ordinal));
        JsonObject body = (JsonObject)JsonNode.Parse(membership.Body)!;
        Assert.Equal("created-1", Assert.Single(Assert.IsType<JsonArray>(body["objects"]))!.GetValue<string>());

        JsonObject create = (JsonObject)JsonNode.Parse(server.Requests.Single(request =>
            request.Method == "POST" && request.Path.EndsWith("/objects", StringComparison.Ordinal)
            && !request.Path.Contains("/lists/", StringComparison.Ordinal)).Body)!;
        Assert.DoesNotContain("collections", create.Select(pair => pair.Key));
    }

    [Fact]
    public async Task UpdateCanOnlyChangeCollectionMembershipWithoutPatchingProperties()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Collection("floor-add", "Niveau ajouté"),
            FakeHomeAnytypeServer.Collection("floor-remove", "Niveau retiré"));

        await Gestures(server).UpdateAsync(
            [new HomeUpdateItem(
                "ZZ",
                null,
                null,
                AddToCollections: ["Niveau ajouté"],
                RemoveFromCollections: ["Niveau retiré"])],
            Ct);

        Assert.Contains(server.Requests, request =>
            request.Method == "POST"
            && request.Path.EndsWith("/lists/floor-add/objects", StringComparison.Ordinal));
        Assert.Contains(server.Requests, request =>
            request.Method == "DELETE"
            && request.Path.EndsWith("/lists/floor-remove/objects/room-zz", StringComparison.Ordinal));
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task CollectionMembershipRejectsAnOrdinaryObjectBeforeWriting()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("ZZ", null, null, AddToCollections: ["ZZ"])], Ct));

        Assert.Contains("n’est pas une collection", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method is "POST" or "PATCH" or "DELETE");
    }

    [Fact]
    public async Task CreateRejectsAValidCodeWhoseRoomIsAbsent()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Outlet,
                [new HomeCreateItem("YY-P01", null, null)], Ct));

        Assert.Contains("Code de pièce inconnu", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task CreateRoomKeepsItsCodeInTheTitleAndUpdatePreservesIt()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Room,
            [new HomeCreateItem("ZZ", "Pièce fictive", null)],
            Ct);

        JsonObject create = (JsonObject)JsonNode.Parse(
            server.Requests.Single(request => request.Method == "POST").Body)!;
        Assert.Equal("ZZ — Pièce fictive", create["name"]!.GetValue<string>());

        await Gestures(server).UpdateAsync(
            [new HomeUpdateItem("ZZ", "Pièce renommée", null)],
            Ct);

        JsonObject update = (JsonObject)JsonNode.Parse(
            server.Requests.Single(request => request.Method == "PATCH").Body)!;
        Assert.Equal("ZZ — Pièce renommée", update["name"]!.GetValue<string>());
    }

    [Fact]
    public void ManagedSchemaUsesTheAppliedCircuitAndBoardKeys()
    {
        Assert.Equal("circuit_elec", HomeSchema.Types.Circuit);
        Assert.Equal("tableau_elec", HomeSchema.Types.DistributionBoard);
        Assert.DoesNotContain(
            ((JsonArray)HomeSchema.CreateRequiredSchemaManifest()["properties"]!).OfType<JsonObject>(),
            property => property["key"]?.GetValue<string>() == "code");
    }

    [Fact]
    public async Task DuplicateElementCodeSuggestsTheNextFreeSequence()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Element("element-1", "ZZ-P01", "room-zz"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Outlet,
                [new HomeCreateItem("ZZ-P01", null, null)], Ct));

        Assert.Contains("ZZ-P02", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task UpdateCannotRewriteCodeOrItsDerivedRoomAndCategory()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Element("element-1", "ZZ-P01", "room-zz"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("ZZ-P01", null, new JsonObject { ["code"] = "ZZ-P02" })], Ct));

        Assert.Contains("immuable", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task DeleteRefusesElementsBeforeAnyDeleteRequest()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Element("element-1", "ZZ-P01", "room-zz"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).DeleteAsync("ZZ-P01", confirm: false, Ct));

        Assert.Contains("Existence", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public async Task MissingRequiredSchemaFailsBeforeInventoryReadOrWrite()
    {
        using var server = new FakeHomeAnytypeServer();
        server.RemoveSchemaProperty(HomeSchema.Properties.Room);

        HomeSchemaException error = await Assert.ThrowsAsync<HomeSchemaException>(() =>
            Gestures(server).GetAsync("anything", Ct));

        Assert.Contains("propriété manquante piece", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Path.EndsWith("/objects", StringComparison.Ordinal));
    }

    private static JsonObject Entry(JsonArray properties, string key) =>
        properties.OfType<JsonObject>().Single(value => value["key"]!.GetValue<string>() == key);
}
