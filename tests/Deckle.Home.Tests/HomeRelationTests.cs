using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "unit")]
public sealed class HomeRelationTests
{
    [Fact]
    public async Task AddPointConnectionPreservesExistingConnections()
    {
        using var server = new FakeHomeAnytypeServer();
        JsonObject device = Device("device-1", "Equipment", "point-1");
        server.SetObjects(device,
            FakeHomeAnytypeServer.Point("point-1", "ZZ-PS01", "First point", "room-1"),
            FakeHomeAnytypeServer.Point("point-2", "ZZ-PS02", "Second point", "room-1"));
        using var api = new AnytypeApiClient(server.Credentials);
        await new HomeGestures(api, FakeHomeAnytypeServer.HomeSpace).UpdateTypedAsync(HomeSchema.Types.Device,
            [new HomeUpdateItem("device-1", null, new JsonObject
            { ["connected_to"] = new JsonObject { ["add"] = new JsonArray("point-2") } })], TestContext.Current.CancellationToken);
        JsonNode patch = JsonNode.Parse(server.Requests.Single(r => r.Method == "PATCH").Body)!;
        Assert.Equal(new[] { "point-1", "point-2" }, patch["properties"]![0]!["objects"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public async Task ConflictingRelationDeltaStopsBeforeWriting()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(Device("device-1", "Equipment"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-PS01", "Point", "room-1"));
        using var api = new AnytypeApiClient(server.Credentials);
        await Assert.ThrowsAsync<ArgumentException>(() => new HomeGestures(api, FakeHomeAnytypeServer.HomeSpace)
            .UpdateAsync([new HomeUpdateItem("device-1", null, new JsonObject
            { ["connected_to"] = new JsonObject { ["add"] = new JsonArray("point-1"), ["remove"] = new JsonArray("Point") } })],
                TestContext.Current.CancellationToken));
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task DeviceConnectsToPointsAndControlsDevicesButNotCircuits()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(Device("device-1", "Controller"), Device("device-2", "Bulb"),
            FakeHomeAnytypeServer.Circuit("circuit-1", "F1"));
        using var api = new AnytypeApiClient(server.Credentials);
        var gestures = new HomeGestures(api, FakeHomeAnytypeServer.HomeSpace);
        await gestures.UpdateAsync([new HomeUpdateItem("device-1", null,
            new JsonObject { ["controls"] = "device-2" })], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gestures.UpdateAsync(
            [new HomeUpdateItem("device-1", null, new JsonObject { ["connected_to"] = "circuit-1" })],
            TestContext.Current.CancellationToken));
        Assert.Single(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task ZoneObjectsUseTheDiscoveredTypeAndAllowNotes()
    {
        using var server = new FakeHomeAnytypeServer();
        server.AddSchemaType("zone-fixture-type", "Zone", "Zones", "collection", "notes");
        using var api = new AnytypeApiClient(server.Credentials);
        var gestures = new HomeGestures(api, FakeHomeAnytypeServer.HomeSpace);
        await gestures.CreateAsync("zone", [new HomeCreateItem(null, "Fictional zone", new JsonObject { ["notes"] = "First" })],
            TestContext.Current.CancellationToken);
        JsonNode create = JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal("zone-fixture-type", create["type_key"]!.GetValue<string>());
        await gestures.UpdateTypedAsync("zone", [new HomeUpdateItem("created-1", null, new JsonObject { ["notes"] = "Updated" })],
            TestContext.Current.CancellationToken);
        Assert.Single(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task OldEarthCheckboxPreventsWritesBeforeFormatMigration()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetSchemaFormat("earthed", "checkbox");
        using var api = new AnytypeApiClient(server.Credentials);
        await Assert.ThrowsAsync<HomeSchemaException>(() => new HomeGestures(api, FakeHomeAnytypeServer.HomeSpace)
            .CreateAsync("device", [new HomeCreateItem(null, "Fictional device", null)], TestContext.Current.CancellationToken));
        Assert.DoesNotContain(server.Requests, r => r.Method == "POST");
    }

    private static JsonObject Device(string id, string name, params string[] points) => new()
    {
        ["id"] = id, ["name"] = name, ["type_key"] = "device",
        ["properties"] = new JsonArray(new JsonObject
        {
            ["key"] = "connected_to", ["format"] = "objects",
            ["objects"] = new JsonArray(points.Select(point => (JsonNode?)JsonValue.Create(point)).ToArray()),
        }),
    };
}
