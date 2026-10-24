using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The handler stands in for the backend; no socket is opened.
[Trait("Category", "unit")]
public class AnytypeV2ClientTests
{
    private const string SpaceId = "space-1";

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AnytypeV2Client ClientOver(FakeV2Handler handler, IBackendEndpointTrust? trust = null) =>
        new(new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", SpaceId), trust, handler);

    private static AnytypeV2Request Get(string path = "objects/o1") => new(HttpMethod.Get, path);

    private static AnytypeV2Request Patch(string? idempotencyKey = null, string? ifMatch = null) =>
        new(HttpMethod.Patch, "objects/o1", new JsonObject { ["ops"] = new JsonArray() },
            IfMatch: ifMatch, IdempotencyKey: idempotencyKey);

    [Fact]
    public async Task ARequestGoesToTheSpacePathUnderV2WithTheBearerAndNoVersionHeader()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);

        await client.SendAsync(Get(), Ct);

        FakeV2Handler.Seen seen = Assert.Single(handler.Requests);
        Assert.Equal($"/v2/spaces/{SpaceId}/objects/o1", seen.PathAndQuery);
        Assert.Equal("Bearer test-bearer", seen.Header("Authorization"));
        Assert.False(seen.HasHeader("Anytype-Version"));
    }

    [Fact]
    public async Task AQueryIsAppendedAndEscaped()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);

        await client.SendAsync(
            new AnytypeV2Request(HttpMethod.Get, "objects/o1", Query: [new("block", "a b&c")]), Ct);

        Assert.EndsWith("?block=a%20b%26c", Assert.Single(handler.Requests).PathAndQuery);
    }

    [Fact]
    public async Task IfMatchAndIdempotencyKeyReachTheWireOnlyWhenGiven()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, "{}")
            .Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);

        await client.SendAsync(Patch(idempotencyKey: "key-1", ifMatch: "\"v1\""), Ct);
        await client.SendAsync(Patch(), Ct);

        Assert.Equal("\"v1\"", handler.Requests[0].Header("If-Match"));
        Assert.Equal("key-1", handler.Requests[0].Header("Idempotency-Key"));
        Assert.False(handler.Requests[1].HasHeader("If-Match"));
        Assert.False(handler.Requests[1].HasHeader("Idempotency-Key"));
    }

    [Fact]
    public async Task TheResponseCarriesTheEtagTheReplayFlagAndTheRemainingBudget()
    {
        var handler = new FakeV2Handler().Reply(
            HttpStatusCode.OK, """{"object":{}}""",
            ("ETag", "\"abc\""), ("Idempotency-Replayed", "true"), ("Ratelimit-Remaining", "41"));
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2Response response = await client.SendAsync(Patch(idempotencyKey: "k"), Ct);

        Assert.Equal(200, response.Status);
        Assert.Equal("\"abc\"", response.ETag);
        Assert.True(response.Replayed);
        Assert.Equal(41, response.RateLimitRemaining);
        Assert.NotNull(response.Body);
    }

    [Fact]
    public async Task AnEmptySuccessBodyIsNullNotAParseError()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.NoContent);
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2Response response = await client.SendAsync(Get(), Ct);

        Assert.Null(response.Body);
        Assert.False(response.Replayed);
        Assert.Null(response.RateLimitRemaining);
    }

    [Fact]
    public async Task ARateLimitedRequestIsRetriedOnceAfterTheAdvertisedWait()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.TooManyRequests, """{"code":"rate_limited"}""", ("Retry-After", "0"))
            .Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);

        // A write with no key: a 429 was refused before execution, so it is
        // replayable all the same.
        await client.SendAsync(Patch(), Ct);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ASecondRateLimitSurfacesAsATypedError()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.TooManyRequests, "{}", ("Retry-After", "0"))
            .Reply(HttpStatusCode.TooManyRequests, "{}", ("Retry-After", "0"));
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2Exception error = await Assert.ThrowsAsync<AnytypeV2Exception>(
            () => client.SendAsync(Patch(), Ct));

        Assert.True(error.IsRateLimited);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AServerErrorIsResentOnlyWhenTheServerCanRecognizeTheRepeat()
    {
        var keyed = new FakeV2Handler()
            .Reply(HttpStatusCode.ServiceUnavailable, "{}", ("Retry-After", "0"))
            .Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client keyedClient = ClientOver(keyed);

        await keyedClient.SendAsync(Patch(idempotencyKey: "key-1"), Ct);

        Assert.Equal(2, keyed.Requests.Count);
        Assert.Equal("key-1", keyed.Requests[0].Header("Idempotency-Key"));
        Assert.Equal("key-1", keyed.Requests[1].Header("Idempotency-Key"));

        var unkeyed = new FakeV2Handler().Reply(HttpStatusCode.ServiceUnavailable, "{}", ("Retry-After", "0"));
        using AnytypeV2Client unkeyedClient = ClientOver(unkeyed);

        AnytypeV2Exception error = await Assert.ThrowsAsync<AnytypeV2Exception>(
            () => unkeyedClient.SendAsync(Patch(), Ct));

        Assert.Equal(503, error.Status);
        Assert.Single(unkeyed.Requests);
    }

    [Fact]
    public async Task AReadIsResentAfterAServerError()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.BadGateway, "{}", ("Retry-After", "0"))
            .Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);

        await client.SendAsync(Get(), Ct);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AClientErrorIsNeverResent()
    {
        var handler = new FakeV2Handler().Reply(
            HttpStatusCode.Conflict, """{"code":"etag_mismatch","message":"stale"}""");
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2Exception error = await Assert.ThrowsAsync<AnytypeV2Exception>(
            () => client.SendAsync(Patch(idempotencyKey: "k", ifMatch: "\"old\""), Ct));

        Assert.True(error.IsStaleEtag);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AnUntrustedEndpointReceivesNothingAndTheBearerStaysHome()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(
            handler, new ScriptedEndpointTrust(BackendEndpointVerdict.Untrusted));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(Get(), Ct));

        Assert.Contains("not owned by a trusted Deckle provider", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AnEmptyPortSaysTheBackendIsNotServingAndSendsNothing()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(
            handler, new ScriptedEndpointTrust(BackendEndpointVerdict.NotListening));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(Get(), Ct));

        Assert.Contains("not serving yet", error.Message);
        Assert.DoesNotContain("trusted Deckle provider", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [Trait("Category", "regression")]
    [InlineData("Retry-After", "86400", 30)]
    [InlineData("Retry-After", "2", 2)]
    [InlineData("Ratelimit-Reset", "3600", 30)]
    [InlineData("Ratelimit-Reset", "-5", 0)]
    [InlineData("Ratelimit-Reset", "1e300", null)]
    public void TheServersWaitIsCappedBeforeItHoldsTheGate(string header, string value, int? expectedSeconds)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (header == "Retry-After") response.Headers.RetryAfter = new(TimeSpan.FromSeconds(double.Parse(value)));
        else response.Headers.TryAddWithoutValidation(header, value);

        TimeSpan? wait = AnytypeV2Client.RetryAfter(response);

        Assert.Equal(expectedSeconds is { } s ? TimeSpan.FromSeconds(s) : null, wait);
    }

    [Fact]
    public async Task ATrustedEndpointIsServedNormally()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler, new ScriptedEndpointTrust(BackendEndpointVerdict.Trusted));

        await client.SendAsync(Get(), Ct);

        Assert.Single(handler.Requests);
    }
}
