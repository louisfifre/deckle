using System.Diagnostics.Tracing;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// The move to the bin hides an object from every later read, so both of its ends
// are in the journal that survives a default configuration: the move that did not
// happen with its reason, the move that did.
[Trait("Category", "observability")]
public sealed class BinJournalTests
{
    const string ObjId = "bafyreiObjaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string OtherId = "bafyreiOthbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    sealed class FakeApproval(DeletionApprovalOutcome outcome) : IDeletionApproval
    {
        public Task<DeletionApprovalOutcome> RequestAsync(DeletionCandidate candidate, CancellationToken ct)
            => Task.FromResult(outcome);
    }

    static ManagementGestures NewGestures(FakeAnytypeServer server, DeletionApprovalOutcome outcome)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new ManagementGestures(client, new NameResolver(client), new FakeApproval(outcome));
    }

    static JsonObject Obj() => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = ObjId,
            ["name"] = "Vieille tache",
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
            ["properties"] = new JsonArray(),
        },
    };

    static JsonObject Rows(params string[] backlinks) => new()
    {
        ["data"] = new JsonArray(new JsonObject
        {
            ["id"] = ObjId,
            ["name"] = "Vieille tache",
            ["type"] = DevSpace.Types.Task,
            ["properties"] = new JsonObject
            {
                ["backlinks"] = new JsonArray(backlinks.Select(id => (JsonNode)JsonValue.Create(id)).ToArray()),
            },
        }),
        ["total"] = 1,
        ["has_more"] = false,
    };

    static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    static bool TravelsInTheJournal(EventWrittenEventArgs e)
        => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Journal) != 0;

    [Fact]
    [Trait("Category", "regression")]
    public async Task ARefusedBinTellsItsReasonInTheJournal()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnV2Search(Rows(OtherId));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, DeletionApprovalOutcome.Approved).DeleteAsync(ObjId, confirm: true, ct: Ct));

        EventWrittenEventArgs refused = Assert.Single(
            listener.Events.Where(TravelsInTheJournal),
            e => e.EventId == DeckleAnytypeSource.EvtDeleteRefused);
        Assert.Equal(DeleteRefusalReasons.Referenced, Field(refused, "reason"));
        Assert.Equal(ObjId, Field(refused, "object_id"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task UnreadableReferencesNameTheFailureThatMadeThemUnreadable()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        // No search route: the fake answers 404, an API refusal with no error code.

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, DeletionApprovalOutcome.Approved).DeleteAsync(ObjId, confirm: true, ct: Ct));

        EventWrittenEventArgs refused = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtDeleteRefused);
        Assert.Equal(DeleteRefusalReasons.ReferencesUnreadable, Field(refused, "reason"));
        Assert.Equal(nameof(AnytypeV2Exception), Field(refused, "ex_type"));
        Assert.Equal("none", Field(refused, "cause"));
    }

    [Fact]
    public async Task AnObjectTheSearchDoesNotListIsToldAsNotListed()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnV2Search(new JsonObject { ["data"] = new JsonArray(), ["total"] = 0, ["has_more"] = false });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, DeletionApprovalOutcome.Approved).DeleteAsync(ObjId, confirm: true, ct: Ct));

        EventWrittenEventArgs refused = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtDeleteRefused);
        Assert.Equal(DeleteRefusalCauses.NotListed, Field(refused, "cause"));
        Assert.Equal("", Field(refused, "ex_type"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ABinThatHappenedIsInTheJournal()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ObjId, Obj());
        server.OnV2Search(Rows());
        server.OnDeleteObject(ObjId, new JsonObject());

        await NewGestures(server, DeletionApprovalOutcome.Approved).DeleteAsync(ObjId, confirm: true, ct: Ct);

        EventWrittenEventArgs binned = Assert.Single(
            listener.Events.Where(TravelsInTheJournal),
            e => e.EventId == DeckleAnytypeSource.EvtObjectBinned);
        Assert.Equal(ObjId, Field(binned, "object_id"));
    }
}