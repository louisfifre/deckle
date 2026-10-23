using System.Diagnostics.Tracing;
using Deckle.Anytype;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Home.Tests;

// A Home space that does not conform to the schema blocks every gesture. The journal
// tells it once, with the reason and a count, never the message that lists what the
// space holds, and tells when the space is repaired.
[Trait("Category", "observability")]
public sealed class HomeSchemaIncidentTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    // A gesture that gets past the schema and then finds no such object: the schema
    // read is what these tests are about, so that refusal is expected.
    private static async Task PastTheSchema(HomeGestures gestures)
    {
        InvalidOperationException refusal = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => gestures.GetAsync("anything", Ct));
        Assert.IsNotType<HomeSchemaException>(refusal);
    }

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    private static bool TravelsInTheJournal(EventWrittenEventArgs e)
        => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Journal) != 0;

    [Fact]
    [Trait("Category", "regression")]
    public async Task ARejectedSchemaIsLoggedByItsCodeAndCountNeverByItsMessage()
    {
        using var listener = new TestEventListener("Deckle-Home");
        using var server = new FakeHomeAnytypeServer();
        server.RemoveSchemaProperty(HomeSchema.Properties.InstalledIn);

        HomeSchemaException error = await Assert.ThrowsAsync<HomeSchemaException>(
            () => Gestures(server).GetAsync("anything", Ct));

        Assert.Contains(HomeSchema.Properties.InstalledIn, error.Message);
        EventWrittenEventArgs detail = Assert.Single(
            listener.Events, e => e.EventId == DeckleHomeSource.EvtSchemaRejectedDetail);
        Assert.Equal("schema_nonconformant", Field(detail, "reason"));
        Assert.True(int.Parse(Field(detail, "failed_checks")) > 0);
        Assert.DoesNotContain(
            listener.Events,
            e => e.Payload!.Any(p => Convert.ToString(p)?.Contains(HomeSchema.Properties.InstalledIn) == true));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ARepairedSpaceClosesTheIncidentWithItsIdAndItsDuration()
    {
        using var listener = new TestEventListener("Deckle-Home");
        using var server = new FakeHomeAnytypeServer();
        server.RemoveSchemaProperty(HomeSchema.Properties.InstalledIn);
        HomeGestures gestures = Gestures(server);

        await Assert.ThrowsAsync<HomeSchemaException>(() => gestures.GetAsync("anything", Ct));
        await Assert.ThrowsAsync<HomeSchemaException>(() => gestures.GetAsync("anything", Ct));
        server.RestoreSchema();
        await PastTheSchema(gestures);

        EventWrittenEventArgs[] journal = listener.Events.Where(TravelsInTheJournal).ToArray();
        EventWrittenEventArgs opened = Assert.Single(
            journal, e => e.EventId == DeckleHomeSource.EvtSchemaUnavailable);
        EventWrittenEventArgs ready = Assert.Single(
            journal, e => e.EventId == DeckleHomeSource.EvtSchemaReady);
        Assert.NotEmpty(Field(opened, "work_id"));
        Assert.Equal(Field(opened, "work_id"), Field(ready, "work_id"));
        Assert.Equal("2", Field(ready, "after_failures"));
    }

    [Fact]
    public async Task AFirstReadThatSucceedsIsInTheJournalWithoutAnIncident()
    {
        using var listener = new TestEventListener("Deckle-Home");
        using var server = new FakeHomeAnytypeServer();

        await PastTheSchema(Gestures(server));

        EventWrittenEventArgs ready = Assert.Single(
            listener.Events.Where(TravelsInTheJournal), e => e.EventId == DeckleHomeSource.EvtSchemaReady);
        Assert.Equal("", Field(ready, "work_id"));
        Assert.Equal("0", Field(ready, "after_failures"));
    }
}