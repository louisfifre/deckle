using System.Net;
using System.Net.Sockets;
using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

// "Load rooms" on the Settings page used to surface HttpClient's own
// "request was canceled due to the configured HttpClient.Timeout" text when
// the stored bridge address went silent, with nothing the person could act
// on. The listing now names the silence so the page can say which address
// did not answer and search for the bridge.
[Trait("Category", "regression")]
public sealed class HueBridgeClientNoAnswerRegressionTests
{
    private static readonly HueBridge Bridge = new("ecb5fafffe000000", "192.168.1.10", 443);
    private static readonly HueCredentials Credentials = new("user", "");

    [Fact]
    public async Task ListingThatTimesOutReportsTheSilentAddress()
    {
        using var http = new HttpClient(new SilentHandler())
        {
            BaseAddress = new Uri("https://192.168.1.10/"),
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        using var client = new HueBridgeClient(Bridge, Credentials, http);

        var ex = await Assert.ThrowsAsync<HueBridgeUnreachableException>(
            () => client.ListGroupsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("192.168.1.10", ex.Message, StringComparison.Ordinal);
        Assert.IsType<TaskCanceledException>(ex.InnerException);
    }

    [Fact]
    public async Task ListingWhoseConnectionFailsIsReportedAsNoAnswer()
    {
        using var http = new HttpClient(new ThrowingHandler(
            new HttpRequestException("No route", new SocketException((int)SocketError.HostUnreachable))))
        {
            BaseAddress = new Uri("https://192.168.1.10/"),
        };
        using var client = new HueBridgeClient(Bridge, Credentials, http);

        await Assert.ThrowsAsync<HueBridgeUnreachableException>(
            () => client.ListGroupsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListingAnsweredWithAnErrorStatusIsNotNoAnswer()
    {
        using var http = new HttpClient(new StatusHandler(HttpStatusCode.InternalServerError))
        {
            BaseAddress = new Uri("https://192.168.1.10/"),
        };
        using var client = new HueBridgeClient(Bridge, Credentials, http);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ListGroupsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task ListingCancelledByTheCallerStaysACancellation()
    {
        using var http = new HttpClient(new SilentHandler())
        {
            BaseAddress = new Uri("https://192.168.1.10/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var client = new HueBridgeClient(Bridge, Credentials, http);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ListGroupsAsync(cts.Token));

        Assert.IsNotType<HueBridgeUnreachableException>(ex);
    }

    // Plays a bridge that accepted the connection and never answers.
    private sealed class SilentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(error);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{}"),
            });
    }
}
