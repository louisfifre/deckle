using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// What the Anytype API client leaves in the log when requests keep being refused:
// one opening line, one closing line with the count, the machine code of each
// refusal and none of the server's text.
[Trait("Category", "observability")]
public sealed class ApiFailureIncidentTests
{
    private sealed class MutableEndpointTrust(bool trusted) : IBackendEndpointTrust
    {
        public bool Trusted { get; set; } = trusted;

        public bool IsTrusted() => Trusted;
    }

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AnytypeV2Client ClientOver(
        FakeV2Handler handler, IBackendEndpointTrust? trust = null) =>
        new(new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"),
            trust, handler);

    private static AnytypeV2Request Get() => new(HttpMethod.Get, "objects/o1");

    private static AnytypeV2Request Patch() =>
        new(HttpMethod.Patch, "objects/o1", new JsonObject { ["ops"] = new JsonArray() },
            IfMatch: "\"v1\"");

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    private static bool TravelsInTheJournal(EventWrittenEventArgs e)
        => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Journal) != 0;

    private const string Envelope =
        """{"status":401,"code":"unauthorized","message":"the key of Alice is not valid","hint":"ask Alice"}""";

    private static async Task Refused(AnytypeV2Client client, AnytypeV2Request request)
        => await Assert.ThrowsAsync<AnytypeV2Exception>(() => client.SendAsync(request, Ct));

    [Fact]
    [Trait("Category", "regression")]
    public async Task FiveIdenticalRefusalsAreOneErrorAndOneClosingLineWithTheCount()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        var handler = new FakeV2Handler();
        for (int i = 0; i < 5; i++) handler.Reply(HttpStatusCode.Unauthorized, Envelope);
        handler.Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);

        for (int i = 0; i < 5; i++) await Refused(client, Get());
        await client.SendAsync(Get(), Ct);

        EventWrittenEventArgs[] journal = listener.Events.Where(TravelsInTheJournal).ToArray();
        EventWrittenEventArgs opened = Assert.Single(
            journal, e => e.EventId == DeckleAnytypeSource.EvtApiRequestFailed);
        EventWrittenEventArgs ended = Assert.Single(
            journal, e => e.EventId == DeckleAnytypeSource.EvtApiRefusalsEnded);
        Assert.Equal(ApiFailureReasons.HttpStatus, Field(opened, "reason"));
        Assert.Equal("401", Field(opened, "status_code"));
        Assert.Equal("5", Field(ended, "refused_requests"));
        Assert.Equal(2, journal.Length);
    }

    [Fact]
    public async Task ARefusalThatChangesStartsARunOfItsOwn()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.Unauthorized, Envelope)
            .Reply(HttpStatusCode.Unauthorized, Envelope)
            .Reply(HttpStatusCode.BadRequest, """{"code":"validation_failed"}""")
            .Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);

        await Refused(client, Get());
        await Refused(client, Get());
        await Refused(client, Get());
        await client.SendAsync(Get(), Ct);

        EventWrittenEventArgs[] opened = listener.Events
            .Where(e => e.EventId == DeckleAnytypeSource.EvtApiRequestFailed).ToArray();
        Assert.Equal(["401", "400"], opened.Select(e => Field(e, "status_code")));
        // Only the run that repeated writes a closing line; the lone 400 is told by its Error.
        EventWrittenEventArgs ended = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtApiRefusalsEnded);
        Assert.Equal("401", Field(ended, "status_code"));
        Assert.Equal("2", Field(ended, "refused_requests"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ARefusedRequestLogsItsErrorCodeAndNothingTheServerSaid()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.BadRequest,
                """{"code":"validation_failed","message":"The title of Alice's note","issues":[{"path":"ops[0]","message":"Alice","hint":"rename Alice"}],"hint":"ask Alice"}""")
            .Reply(HttpStatusCode.Forbidden, "<html>proxy page naming Alice</html>");
        using AnytypeV2Client client = ClientOver(handler);

        await Refused(client, Get());
        await Refused(client, Get());

        EventWrittenEventArgs[] details = listener.Events
            .Where(e => e.EventId == DeckleAnytypeSource.EvtApiRequestFailedDetail).ToArray();
        Assert.Equal(["validation_failed", "none"], details.Select(e => Field(e, "error")));
        Assert.DoesNotContain(
            listener.Events,
            e => e.Payload!.Any(p => Convert.ToString(p)?.Contains("Alice") == true));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ARateLimitThatSurvivesItsRetryReachesTheJournalWithoutVerboseTracing()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.TooManyRequests, """{"code":"rate_limited"}""", ("Retry-After", "0"))
            .Reply(HttpStatusCode.TooManyRequests, """{"code":"rate_limited"}""", ("Retry-After", "0"));
        using AnytypeV2Client client = ClientOver(handler);

        await Refused(client, Get());

        EventWrittenEventArgs failed = Assert.Single(
            listener.Events.Where(TravelsInTheJournal),
            e => e.EventId == DeckleAnytypeSource.EvtApiRequestFailed);
        Assert.Equal("429", Field(failed, "status_code"));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AStaleVersionTheCallerWillRetryIsNotAnError()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        var handler = new FakeV2Handler().Reply(
            HttpStatusCode.Conflict, """{"code":"etag_mismatch","message":"stale"}""");
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2Exception ex = await Assert.ThrowsAsync<AnytypeV2Exception>(
            () => client.SendAsync(Patch(), Ct));

        Assert.True(ex.IsStaleEtag);
        Assert.DoesNotContain(listener.Events, TravelsInTheJournal);
        EventWrittenEventArgs superseded = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtApiRequestSuperseded);
        Assert.Equal("etag_mismatch", Field(superseded, "code"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ARunOfRequestsToAnUntrustedEndpointIsOneErrorUntilTheOwnerIsTrusted()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        var trust = new MutableEndpointTrust(trusted: false);
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler, trust);

        for (int i = 0; i < 3; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(Get(), Ct));
        trust.Trusted = true;
        await client.SendAsync(Get(), Ct);

        EventWrittenEventArgs opened = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtApiRequestFailed);
        Assert.Equal(ApiFailureReasons.UntrustedEndpoint, Field(opened, "reason"));
        EventWrittenEventArgs ended = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtApiRefusalsEnded);
        Assert.Equal("3", Field(ended, "refused_requests"));
        Assert.Equal(2, listener.Events.Count(TravelsInTheJournal));
    }
}
