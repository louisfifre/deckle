using System.Net;
using System.Net.Http;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The handler stands in for the backend; no socket is opened.
[Trait("Category", "unit")]
public class AnytypeRequestGateTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AnytypeCredentials Credentials =
        new("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1");

    // Answers every request after a pause and remembers how many were on the
    // wire at once. The pause is what gives an unshared gate the time to let a
    // second request in; a shared gate keeps the peak at one however long it is.
    private sealed class OverlapProbeHandler : HttpMessageHandler
    {
        private int _active;
        public int Peak { get; private set; }
        public int Served { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int now = Interlocked.Increment(ref _active);
            lock (this) Peak = Math.Max(Peak, now);
            await Task.Delay(100, cancellationToken);
            Interlocked.Decrement(ref _active);
            lock (this) Served++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"object\":{\"id\":\"o1\"}}", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task AV1AndAV2CallNeverReachTheBackendAtTheSameTime()
    {
        var handler = new OverlapProbeHandler();
        using var client = new AnytypeApiClient(Credentials, endpointTrust: null, handler);

        await Task.WhenAll(
            client.GetObjectAsync("o1", Ct),
            client.V2.SendAsync(new AnytypeV2Request(HttpMethod.Get, "objects/o1"), Ct));

        Assert.Equal(2, handler.Served);
        Assert.Equal(1, handler.Peak);
    }

    [Fact]
    public async Task ADisposedV2ClientLeavesTheSharedGateToItsOwner()
    {
        var handler = new OverlapProbeHandler();
        using var client = new AnytypeApiClient(Credentials, endpointTrust: null, handler);
        client.V2.Dispose();

        // Reaching the gate again would throw ObjectDisposedException if the
        // borrower had disposed it.
        await client.GetObjectAsync("o1", Ct);

        Assert.Equal(1, handler.Served);
    }
}
