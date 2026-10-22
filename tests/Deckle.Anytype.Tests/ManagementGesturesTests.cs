using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for ManagementGestures.DeleteAsync over the shared
// FakeAnytypeServer. They pin the two-step contract: the first (preview) call
// looks the target up and sends NO DELETE; only the confirmed call, once the
// person approved and nothing links to the object, moves it to the bin.
// Selector is a bafy* id so the resolver short-circuits.
[Trait("Category", "integration")]
public class ManagementGesturesTests
{
    const string ObjId = "bafyreiObjaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string OtherId = "bafyreiOthbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    sealed class FakeApproval(DeletionApprovalOutcome outcome) : IDeletionApproval
    {
        public int Asked { get; private set; }

        public Task<DeletionApprovalOutcome> RequestAsync(DeletionCandidate candidate, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(outcome);
        }
    }

    static ManagementGestures NewGestures(FakeAnytypeServer server, IDeletionApproval? approval = null)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new ManagementGestures(
            client, new NameResolver(client), approval ?? new FakeApproval(DeletionApprovalOutcome.Approved));
    }

    static JsonObject Obj(string type = DevSpace.Types.Task) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = ObjId,
            ["name"] = "Vieille tâche",
            ["type"] = new JsonObject { ["key"] = type },
            ["snippet"] = "Notes diverses",
            ["properties"] = new JsonArray(),
        },
    };

    // The row the v2 search serves for the id filter, with its backlinks.
    static JsonObject Backlinks(params string[] ids) => new()
    {
        ["data"] = new JsonArray(new JsonObject
        {
            ["id"] = ObjId,
            ["name"] = "Vieille tâche",
            ["type"] = DevSpace.Types.Task,
            ["properties"] = new JsonObject
            {
                ["backlinks"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)).ToArray()),
            },
        }),
        ["total"] = 1,
        ["has_more"] = false,
    };

    static bool Deleted(FakeAnytypeServer server) => server.Requests.Any(r => r.Method == "DELETE");

    [Fact]
    public async Task PreviewReturnsTheTargetIdentityAndSendsNoDelete()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnV2Search(Backlinks());
        var approval = new FakeApproval(DeletionApprovalOutcome.Approved);

        string digest = await NewGestures(server, approval).DeleteAsync(ObjId, ct: Ct);

        Assert.Contains(ObjId, digest);
        Assert.Contains("confirm", digest);
        Assert.False(Deleted(server));
        Assert.Equal(0, approval.Asked);
    }

    [Fact]
    public async Task ApprovedCallMovesAnUnlinkedObjectToTheBin()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnV2Search(Backlinks(ObjId)); // a self-reference links nothing else
        server.OnDeleteObject(ObjId, new JsonObject()); // empty body is tolerated
        var approval = new FakeApproval(DeletionApprovalOutcome.Approved);

        string digest = await NewGestures(server, approval).DeleteAsync(ObjId, confirm: true, ct: Ct);

        Assert.Equal(1, approval.Asked);
        Assert.Contains(server.Requests,
            r => r.Method == "DELETE" && r.Path.EndsWith($"/objects/{ObjId}"));
        Assert.Contains("corbeille", digest);
    }

    [Theory]
    [InlineData(DeletionApprovalOutcome.Declined)]
    [InlineData(DeletionApprovalOutcome.Unanswered)]
    [InlineData(DeletionApprovalOutcome.Unavailable)]
    public async Task ConfirmedCallWithoutThePersonsApprovalDeletesNothing(DeletionApprovalOutcome outcome)
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnV2Search(Backlinks());
        server.OnDeleteObject(ObjId, new JsonObject());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, new FakeApproval(outcome)).DeleteAsync(ObjId, confirm: true, ct: Ct));

        Assert.False(Deleted(server));
    }

    [Fact]
    public async Task AnUnlinkedRapportIsPreviewedThenBinnedAfterApproval()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj(DevSpace.Types.Rapport));
        server.OnV2Search(Backlinks()); // served again for every links read
        server.OnDeleteObject(ObjId, new JsonObject());
        var approval = new FakeApproval(DeletionApprovalOutcome.Approved);
        ManagementGestures gestures = NewGestures(server, approval);

        string preview = await gestures.DeleteAsync(ObjId, ct: Ct);

        Assert.Contains(ObjId, preview);
        Assert.False(Deleted(server));
        Assert.Equal(0, approval.Asked);

        string digest = await gestures.DeleteAsync(ObjId, confirm: true, ct: Ct);

        Assert.Equal(1, approval.Asked);
        Assert.Contains(server.Requests,
            r => r.Method == "DELETE" && r.Path.EndsWith($"/objects/{ObjId}"));
        Assert.Contains("corbeille", digest);
    }

    [Fact]
    public async Task ARapportAnotherObjectLinksToIsRefusedLikeAnyObject()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj(DevSpace.Types.Rapport));
        server.OnV2Search(Backlinks(OtherId));
        server.OnDeleteObject(ObjId, new JsonObject());
        var approval = new FakeApproval(DeletionApprovalOutcome.Approved);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, approval).DeleteAsync(ObjId, confirm: true, ct: Ct));

        Assert.Equal(0, approval.Asked);
        Assert.False(Deleted(server));
    }

    [Fact]
    public async Task AnObjectAnotherObjectLinksToIsRefusedAndArchiveIsOffered()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnV2Search(Backlinks(OtherId));
        server.OnDeleteObject(ObjId, new JsonObject());
        var approval = new FakeApproval(DeletionApprovalOutcome.Approved);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, approval).DeleteAsync(ObjId, confirm: true, ct: Ct));

        Assert.Contains("archive", error.Message);
        Assert.Equal(0, approval.Asked);
        Assert.False(Deleted(server));
    }

    [Fact]
    public async Task UnreadableLinksRefuseTheDelete()
    {
        // No v2 search route: the fake answers 404, as a failing search would.
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnDeleteObject(ObjId, new JsonObject());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).DeleteAsync(ObjId, confirm: true, ct: Ct));

        Assert.False(Deleted(server));
    }

    [Fact]
    public async Task ALinkAddedWhileThePersonAnsweredStillRefusesTheDelete()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnV2Search(Backlinks());        // before the toast
        server.OnV2Search(Backlinks(OtherId)); // read again after the approval
        server.OnDeleteObject(ObjId, new JsonObject());
        var approval = new FakeApproval(DeletionApprovalOutcome.Approved);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, approval).DeleteAsync(ObjId, confirm: true, ct: Ct));

        Assert.Equal(1, approval.Asked);
        Assert.False(Deleted(server));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ConfirmedCallRejectsANameInsteadOfThePreviewedIdWithoutDeleting()
    {
        using var server = new FakeAnytypeServer();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).DeleteAsync("Vieille tâche", confirm: true, ct: Ct));

        Assert.Contains("id Anytype stable", error.Message);
        Assert.Empty(server.Requests);
    }
}
