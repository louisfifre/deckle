using System.Net;
using System.Net.Http;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// The health path tells a supervisor which key the gateway holds, as a digest:
// the key itself must never be on the wire.
[Trait("Category", "integration")]
public sealed class McpHealthEndpointTests
{
    private const string ApiKey = "health-test-api-key-0123456789";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (McpHttpHost Host, AnytypeApiClient Api, int Port) Start(string? fingerprint)
    {
        var tokens = new McpClientTokens(new FakeSecretVault(), [.. McpClients.All]);
        tokens.EnsureMinted();
        var api = new AnytypeApiClient(new AnytypeCredentials(
            "http://127.0.0.1:1", "2025-11-08", ApiKey, "dummy-space"));
        for (int attempt = 0; attempt < 32; attempt++)
        {
            int port = Random.Shared.Next(49152, 49152 + 16384);
            var host = new McpHttpHost(api, tokens, port, fingerprint);
            if (host.Start()) return (host, api, port);
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        api.Dispose();
        throw new InvalidOperationException("No free loopback port found for the MCP test host.");
    }

    [Fact]
    public async Task The_health_path_answers_the_fingerprint_without_a_bearer_and_never_the_key()
    {
        string fingerprint = GatewayKeyFingerprint.Of(ApiKey);
        (McpHttpHost host, AnytypeApiClient api, int port) = Start(fingerprint);
        try
        {
            using var http = new HttpClient();
            using HttpResponseMessage response = await http.GetAsync(
                $"http://127.0.0.1:{port}{GatewayContract.HealthPath}", Ct);
            string body = await response.Content.ReadAsStringAsync(Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(fingerprint, body, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, body, StringComparison.Ordinal);
        }
        finally
        {
            await host.DisposeAsync();
            api.Dispose();
        }
    }

    [Fact]
    public async Task A_page_from_another_origin_gets_no_fingerprint()
    {
        (McpHttpHost host, AnytypeApiClient api, int port) = Start(GatewayKeyFingerprint.Of(ApiKey));
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}{GatewayContract.HealthPath}");
            request.Headers.TryAddWithoutValidation("Origin", "https://example.com");
            using HttpResponseMessage response = await http.SendAsync(request, Ct);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
            api.Dispose();
        }
    }

    [Fact]
    public async Task A_host_given_no_fingerprint_serves_no_health_answer()
    {
        (McpHttpHost host, AnytypeApiClient api, int port) = Start(null);
        try
        {
            using var http = new HttpClient();
            using HttpResponseMessage response = await http.GetAsync(
                $"http://127.0.0.1:{port}{GatewayContract.HealthPath}", Ct);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
            api.Dispose();
        }
    }
}