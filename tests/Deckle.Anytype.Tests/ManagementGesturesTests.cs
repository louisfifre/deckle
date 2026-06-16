using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for ManagementGestures.DeleteAsync over the shared
// FakeAnytypeServer. They pin the two-step contract: the first (preview) call
// looks the target up and sends NO DELETE; only the confirmed call moves the
// object to the bin. Selector is a bafy* id so the resolver short-circuits.
[Trait("Category", "integration")]
public class ManagementGesturesTests
{
    const string ObjId = "bafyreiObjaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    static ManagementGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new ManagementGestures(client, new NameResolver(client));
    }

    static JsonObject Obj() => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = ObjId,
            ["name"] = "Vieille tâche",
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
            ["snippet"] = "Notes diverses",
            ["properties"] = new JsonArray(),
        },
    };

    [Fact]
    public async Task PreviewReturnsTheTargetIdentityAndSendsNoDelete()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());

        string digest = await NewGestures(server).DeleteAsync(ObjId);

        // The preview spells out the id (the confirmation handle) and the recall.
        Assert.Contains(ObjId, digest);
        Assert.Contains("confirm", digest);
        // Nothing was trashed: no DELETE left the gesture.
        Assert.DoesNotContain(server.Requests, r => r.Method == "DELETE");
    }

    [Fact]
    public async Task ConfirmedCallMovesTheObjectToTheBin()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnDeleteObject(ObjId, new JsonObject()); // empty body is tolerated

        string digest = await NewGestures(server).DeleteAsync(ObjId, confirm: true);

        Assert.Contains(server.Requests,
            r => r.Method == "DELETE" && r.Path.EndsWith($"/objects/{ObjId}"));
        Assert.Contains("corbeille", digest);
    }
}
