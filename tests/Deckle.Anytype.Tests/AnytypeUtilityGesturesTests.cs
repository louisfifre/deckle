using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "integration")]
public class AnytypeUtilityGesturesTests
{
    private const string CollectionId = "bafycollection000000000000000000000000000000000000000000000";
    private const string ObjectId = "bafyobject000000000000000000000000000000000000000000000000";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AnytypeSpaceAliases Aliases() => new(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = FakeAnytypeServer.Space,
        });

    private static JsonObject Page(JsonArray data) => new()
    {
        ["data"] = data,
        ["pagination"] = new JsonObject { ["has_more"] = false },
    };

    // GET and PATCH on an object answer the object wrapped in {object:{…}}; the
    // fake serves a canned body verbatim, so the envelope belongs to the fixture.
    private static JsonObject Enveloped(JsonObject value) => new() { ["object"] = value };

    [Fact]
    public async Task CollectionAddResolvesAndValidatesEverythingBeforeOneWrite()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(Page(new JsonArray
        {
            new JsonObject { ["id"] = CollectionId, ["name"] = "Rez-de-chaussée" },
        }));
        server.OnSearch(Page(new JsonArray
        {
            new JsonObject { ["id"] = ObjectId, ["name"] = "Cuisine" },
        }));
        server.OnGetObject(CollectionId, Enveloped(new JsonObject
        {
            ["id"] = CollectionId,
            ["name"] = "Rez-de-chaussée",
            ["layout"] = "collection",
        }));
        server.OnGetObject(ObjectId, Enveloped(
            new JsonObject { ["id"] = ObjectId, ["name"] = "Cuisine" }));
        server.OnPostListObjects(CollectionId, "\"Objects added successfully\"");

        var api = new AnytypeApiClient(server.Credentials);
        var gestures = new CollectionMembershipGestures(api, Aliases(), new NameResolver(api));

        string result = await gestures.AddAsync(
            "home", "Rez-de-chaussée", ["Cuisine", "Cuisine"], Ct);

        Assert.Contains("1 objet(s) ajouté(s)", result);
        FakeAnytypeServer.Received write = Assert.Single(
            server.Requests,
            request => request.Method == "POST"
                && request.Path.EndsWith($"/lists/{CollectionId}/objects", StringComparison.Ordinal));
        JsonObject body = Assert.IsType<JsonObject>(JsonNode.Parse(write.Body));
        JsonArray members = Assert.IsType<JsonArray>(body["objects"]);
        Assert.Equal([ObjectId], members.Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public async Task CollectionAddRejectsANonCollectionBeforeWriting()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(CollectionId, Enveloped(new JsonObject
        {
            ["id"] = CollectionId,
            ["name"] = "Cuisine",
            ["layout"] = "basic",
        }));

        var api = new AnytypeApiClient(server.Credentials);
        var gestures = new CollectionMembershipGestures(api, Aliases(), new NameResolver(api));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gestures.AddAsync("home", CollectionId, [ObjectId], Ct));

        Assert.Contains("n’est pas une collection", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task SelectSetWritesOneLiveTagKey()
    {
        using var server = SelectServer("select");
        var api = new AnytypeApiClient(server.Credentials);
        var gestures = new SelectValueGestures(api, Aliases(), new NameResolver(api));

        string result = await gestures.SetAsync(
            "home", ObjectId, "existence", ["existant"], Ct);

        Assert.Contains("existence = existant", result);
        JsonObject body = server.LastBodyFor("PATCH");
        JsonObject property = Assert.IsType<JsonObject>(
            Assert.IsType<JsonArray>(body["properties"]).Single());
        Assert.Equal("existence", property["key"]!.GetValue<string>());
        Assert.Equal("existant", property["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task SelectSetWritesCompleteMultiSelectKeyList()
    {
        using var server = SelectServer("multi_select", ("urgent", "Urgent"), ("electricite", "Électricité"));
        var api = new AnytypeApiClient(server.Credentials);
        var gestures = new SelectValueGestures(api, Aliases(), new NameResolver(api));

        await gestures.SetAsync(
            "home", ObjectId, "tags", ["urgent", "electricite"], Ct);

        JsonObject body = server.LastBodyFor("PATCH");
        JsonObject property = Assert.IsType<JsonObject>(
            Assert.IsType<JsonArray>(body["properties"]).Single());
        JsonArray values = Assert.IsType<JsonArray>(property["multi_select"]);
        Assert.Equal(["urgent", "electricite"], values.Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public async Task SelectSetRejectsUnknownTagKeyBeforePatch()
    {
        using var server = SelectServer("select");
        var api = new AnytypeApiClient(server.Credentials);
        var gestures = new SelectValueGestures(api, Aliases(), new NameResolver(api));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gestures.SetAsync("home", ObjectId, "existence", ["missing"], Ct));

        Assert.Contains("Clé de tag inconnue", error.Message);
        Assert.Contains("existant (Existant)", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    private static FakeAnytypeServer SelectServer(
        string format,
        params (string Key, string Name)[] tags)
    {
        if (tags.Length == 0) tags = [("existant", "Existant")];

        var server = new FakeAnytypeServer();
        server.OnGetObject(ObjectId, Enveloped(
            new JsonObject { ["id"] = ObjectId, ["name"] = "Objet" }));
        server.OnListTypes(Page(new JsonArray()));
        server.OnListProperties(Page(new JsonArray
        {
            new JsonObject
            {
                ["id"] = "property-select",
                ["key"] = format == "select" ? "existence" : "tags",
                ["name"] = "Valeurs",
                ["format"] = format,
            },
        }));
        var data = new JsonArray();
        foreach ((string key, string name) in tags)
            data.Add(new JsonObject
            {
                ["id"] = "tag-" + key,
                ["key"] = key,
                ["name"] = name,
                ["color"] = "grey",
            });
        server.OnListPropertyTags("property-select", Page(data));
        server.OnPatchObject(ObjectId, Enveloped(new JsonObject { ["id"] = ObjectId }));
        return server;
    }
}
