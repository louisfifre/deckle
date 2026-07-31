using System.Net;
using System.Text;
using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "unit")]
public sealed class HueBridgeClientIdentityTests
{
    [Fact]
    public async Task ReadsCanonicalBridgeIdFromConfiguration()
    {
        using var http = CreateHttpClient("""{"bridgeid":" ECB5FAFFFE25B9B5 "}""");
        using var client = new HueBridgeClient(
            new HueBridge("manual", "192.168.1.10", 443),
            http);

        var bridgeId = await client.GetBridgeIdAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ECB5FAFFFE25B9B5", bridgeId);
    }

    [Fact]
    public async Task RejectsConfigurationWithoutBridgeId()
    {
        using var http = CreateHttpClient("{}");
        using var client = new HueBridgeClient(
            new HueBridge("manual", "192.168.1.10", 443),
            http);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetBridgeIdAsync(TestContext.Current.CancellationToken));
    }

    private static HttpClient CreateHttpClient(string content)
        => new(new StubHandler(content))
        {
            BaseAddress = new Uri("https://192.168.1.10/"),
        };

    private sealed class StubHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("api/config", request.RequestUri?.PathAndQuery.TrimStart('/'));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
    }
}
