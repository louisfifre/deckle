using System.Diagnostics.Tracing;
using Deckle.Anytype;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Home.Tests;

// A move to the bin hides a Home object from every later read, so the person is
// asked outside the chat, like for an Anytype object, and the delete fails closed
// when they refuse, do not answer or cannot be asked.
[Trait("Category", "integration")]
public sealed class HomeDeleteApprovalTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static HomeGestures Gestures(FakeHomeAnytypeServer server, IDeletionApproval? approval) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace, approval);

    static FakeHomeAnytypeServer ServerWithAFreePoint()
    {
        var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("bafyreiroomzzaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Point("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ZZ-PS01", "Prise saisie par erreur", "bafyreiroomzzaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        return server;
    }

    static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    static bool TravelsInTheLog(EventWrittenEventArgs e)
        => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Milestone) != 0;

    [Fact]
    public async Task AnApprovedDeleteMovesTheObjectToTheBin()
    {
        using var server = ServerWithAFreePoint();
        var approval = new FakeDeletionApproval(DeletionApprovalOutcome.Approved);

        string result = await Gestures(server, approval).DeleteAsync("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", confirm: true, Ct);

        Assert.Contains("corbeille", result);
        DeletionCandidate asked = Assert.Single(approval.Asked);
        Assert.Equal("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", asked.Id);
        Assert.Equal(HomeSchema.Types.Point, asked.TypeKey);
        Assert.Contains(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public async Task ThePreviewDoesNotAskThePerson()
    {
        using var server = ServerWithAFreePoint();
        var approval = new FakeDeletionApproval(DeletionApprovalOutcome.Approved);

        string preview = await Gestures(server, approval).DeleteAsync("ZZ-PS01", confirm: false, Ct);

        Assert.Contains("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", preview);
        Assert.Contains("notification Windows", preview);
        Assert.Empty(approval.Asked);
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }

    [Theory]
    [InlineData(DeletionApprovalOutcome.Declined, "répondu non")]
    [InlineData(DeletionApprovalOutcome.Unanswered, "pas de réponse")]
    [InlineData(DeletionApprovalOutcome.Unavailable, "ne peut pas demander")]
    [Trait("Category", "regression")]
    public async Task ADeleteTheApprovalDoesNotGrantIsRefusedAndNothingMoves(
        DeletionApprovalOutcome outcome, string expectedWording)
    {
        using var server = ServerWithAFreePoint();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server, new FakeDeletionApproval(outcome)).DeleteAsync("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", confirm: true, Ct));

        Assert.Contains(expectedWording, error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AnApprovalThatTimesOutReadsAsUnansweredAndNothingMoves()
    {
        using var server = ServerWithAFreePoint();
        var approval = new FakeDeletionApproval(DeletionApprovalOutcome.Approved, neverAnswersInTime: true);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server, approval).DeleteAsync("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", confirm: true, Ct));

        Assert.Contains("pas de réponse", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AHostThatInstalledNoApprovalFailsClosed()
    {
        using var server = ServerWithAFreePoint();

        // Built with no approval of its own, it takes the host's default, which
        // answers Unavailable until a host installs the Windows notification.
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server, approval: null).DeleteAsync("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", confirm: true, Ct));

        Assert.Contains("ne peut pas demander", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }
    [Fact]
    [Trait("Category", "regression")]
    public async Task AReferenceAddedWhileThePersonWasAskedStillStopsTheDelete()
    {
        using var server = ServerWithAFreePoint();
        var approval = new FakeDeletionApproval(
            DeletionApprovalOutcome.Approved,
            whileAsking: () => server.SetObjects(
                FakeHomeAnytypeServer.Room("bafyreiroomzzaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ZZ", "Pièce fictive"),
                FakeHomeAnytypeServer.Point("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ZZ-PS01", "Prise saisie par erreur", "bafyreiroomzzaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                FakeHomeAnytypeServer.Point("bafyreipoint2aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ZZ-CS01", "Interrupteur fictif", "bafyreiroomzzaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server, approval).DeleteAsync("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", confirm: true, Ct));

        Assert.Contains("référencé", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public async Task AReferencedObjectIsRefusedBeforeThePersonIsAsked()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("bafyreisitebaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Salle de bain fictive", "bafyreitodobaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            FakeHomeAnytypeServer.Todo("bafyreitodobaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Déposer le lavabo fictif", "bafyreisitebaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", done: true));
        var approval = new FakeDeletionApproval(DeletionApprovalOutcome.Approved);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server, approval).DeleteAsync("bafyreisitebaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", confirm: true, Ct));

        Assert.Empty(approval.Asked);
    }

    [Fact]
    [Trait("Category", "observability")]
    public async Task ARefusedDeleteTellsItsReasonAndNeverTheObjectInTheLog()
    {
        using var listener = new TestEventListener("Deckle-Home");
        using var server = ServerWithAFreePoint();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server, new FakeDeletionApproval(DeletionApprovalOutcome.Declined))
                .DeleteAsync("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", confirm: true, Ct));

        EventWrittenEventArgs refused = Assert.Single(
            listener.Events.Where(TravelsInTheLog),
            e => e.EventId == DeckleHomeSource.EvtDeleteRefused);
        Assert.Equal("declined", Field(refused, "reason"));
        Assert.Equal(HomeSchema.Types.Point, Field(refused, "type_key"));
        Assert.DoesNotContain(listener.Events, e => e.Payload!.Any(p =>
            Convert.ToString(p) is { } text
            && (text.Contains("Prise saisie par erreur") || text.Contains("ZZ-PS01") || text.Contains("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"))));
    }

    [Fact]
    [Trait("Category", "observability")]
    public async Task ABinThatHappenedIsInTheLogWithoutTheObject()
    {
        using var listener = new TestEventListener("Deckle-Home");
        using var server = ServerWithAFreePoint();

        await Gestures(server, new FakeDeletionApproval(DeletionApprovalOutcome.Approved))
            .DeleteAsync("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", confirm: true, Ct);

        EventWrittenEventArgs binned = Assert.Single(
            listener.Events.Where(TravelsInTheLog),
            e => e.EventId == DeckleHomeSource.EvtObjectBinned);
        Assert.Equal(HomeSchema.Types.Point, Field(binned, "type_key"));
        Assert.DoesNotContain(listener.Events, e => e.Payload!.Any(p =>
            Convert.ToString(p) is { } text
            && (text.Contains("Prise saisie par erreur") || text.Contains("ZZ-PS01") || text.Contains("bafyreipointbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"))));
    }
}