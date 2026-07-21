using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// End-to-end tests for McpHttpHost over a real loopback HttpListener driven by a
// real HttpClient. Hermetic: a FakeSecretVault mints the bearers, the host binds a
// per-test loopback port far from the production 33255, and the AnytypeApiClient
// points at a dead port. initialize and tools/list never touch the API, so no
// backend is needed — tests deliberately avoid tool CALLS, which would reach the
// dead port.
//
// These pin the transport's own voice: the auth challenge, the DNS-rebinding
// origin guard, session opening/routing/teardown, the method allow-list, and the
// JSON-RPC vs HTTP channel split (a delivered-but-invalid body rides a 200 with a
// JSON-RPC error object; a transport-level refusal is an HTTP status).
[Trait("Category", "unit")]
public class McpHttpHostTests
{
    // A running host with its bearers already read out of the fake vault, so a test
    // can present the right token for each client. IAsyncDisposable so `await using`
    // tears the listener down.
    sealed class Harness : IAsyncDisposable
    {
        public McpHttpHost Host { get; }
        public HttpClient Client { get; }
        public string BaseUrl => Host.BaseUrl;
        public string ClaudeBearer { get; }
        public string CodexBearer { get; }
        public string HomeBearer { get; }

        private Harness(McpHttpHost host, string claude, string codex, string home)
        {
            Host = host;
            Client = new HttpClient();
            ClaudeBearer = claude;
            CodexBearer = codex;
            HomeBearer = home;
        }

        // Build the whole stack and bind a free loopback port, walking up from a base
        // far from production on a taken port (Start returns false when the bind fails).
        public static Harness Start()
        {
            var vault = new FakeSecretVault();
            var tokens = new McpClientTokens(vault);
            tokens.EnsureMinted();

            vault.TryGet(McpClients.Claude.TokenSecretName, out string? claude);
            vault.TryGet(McpClients.Codex.TokenSecretName, out string? codex);
            vault.TryGet(McpClients.Home.TokenSecretName, out string? home);

            // The API client never sees a live backend: a dead port so a stray tool
            // call would fail fast rather than hang. initialize/tools/list never dial it.
            var api = new AnytypeApiClient(new AnytypeCredentials(
                "http://127.0.0.1:1", "2025-11-08", "dummy-key", "dummy-space"));

            for (int port = 34611; port < 34611 + 40; port++)
            {
                var host = new McpHttpHost(api, tokens, port);
                if (host.Start())
                    return new Harness(host, claude!, codex!, home!);

                // Bind failed (port taken): dispose and try the next.
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            throw new InvalidOperationException("No free loopback port found for the test host.");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Host.DisposeAsync();
        }
    }

    // ── Request builders ──────────────────────────────────────────────────────

    static HttpRequestMessage Post(string url, string? bearer, string? body,
        string? sessionId = null, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (bearer is not null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
        if (sessionId is not null)
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        if (origin is not null)
            request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }

    static string InitializeBody(int id = 1, string version = "2025-11-25") =>
        $$$"""{"jsonrpc":"2.0","id":{{{id}}},"method":"initialize","params":{"protocolVersion":"{{{version}}}"}}""";

    const string ToolsListBody = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static async Task<JsonObject> BodyJson(HttpResponseMessage response) =>
        (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;

    // Open a session for one bearer and return its id from the response header, so a
    // follow-up can route on it. Used by every session-scoped test.
    static async Task<string> OpenSession(Harness h, string bearer)
    {
        using var response = await h.Client.SendAsync(Post(h.BaseUrl, bearer, InitializeBody()), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.GetValues("Mcp-Session-Id").Single();
    }

    static string[] ToolNames(JsonObject toolsListResult)
    {
        var tools = (JsonArray)((JsonObject)toolsListResult["result"]!)["tools"]!;
        return tools.Select(t => ((JsonObject)t!)["name"]!.GetValue<string>()).ToArray();
    }

    // ── auth ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestWithoutBearerIsChallengedWith401()
    {
        await using var h = Harness.Start();

        using var response = await h.Client.SendAsync(Post(h.BaseUrl, bearer: null, InitializeBody()), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, v => v.Scheme == "Bearer");
    }

    [Fact]
    public async Task RequestWithUnknownBearerIs401()
    {
        await using var h = Harness.Start();

        using var response = await h.Client.SendAsync(Post(h.BaseUrl, "not-a-real-token", InitializeBody()), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── origin guard ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ForeignOriginIsRefusedWith403WhileAbsentOriginPasses()
    {
        await using var h = Harness.Start();

        // A present, cross-site Origin is the DNS-rebinding case the spec guards.
        using var foreign = await h.Client.SendAsync(
            Post(h.BaseUrl, h.ClaudeBearer, InitializeBody(), origin: "http://evil.example"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        // A non-browser client sends no Origin at all — that must pass through.
        using var absent = await h.Client.SendAsync(
            Post(h.BaseUrl, h.ClaudeBearer, InitializeBody()), Ct);
        Assert.Equal(HttpStatusCode.OK, absent.StatusCode);
    }

    // ── initialize / session opening ────────────────────────────────────────────

    [Fact]
    public async Task InitializeReturns200WithSessionHeaderAndNegotiatedVersion()
    {
        await using var h = Harness.Start();

        using var response = await h.Client.SendAsync(
            Post(h.BaseUrl, h.ClaudeBearer, InitializeBody(version: "2025-06-18")), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Mcp-Session-Id"));

        JsonObject body = await BodyJson(response);
        JsonObject result = (JsonObject)body["result"]!;
        Assert.Equal("2025-06-18", result["protocolVersion"]!.GetValue<string>());
    }

    // ── tools/list per client surface ───────────────────────────────────────────

    [Fact]
    public async Task ClaudeSessionListsAManagementTool()
    {
        await using var h = Harness.Start();
        string session = await OpenSession(h, h.ClaudeBearer);

        using var response = await h.Client.SendAsync(
            Post(h.BaseUrl, h.ClaudeBearer, ToolsListBody, sessionId: session), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // claude is the supervised profile: the destructive delete tool is mounted.
        Assert.Contains("delete", ToolNames(await BodyJson(response)));
    }

    [Fact]
    public async Task CodexSessionListsDialoguesAndNoManagementTool()
    {
        await using var h = Harness.Start();
        string session = await OpenSession(h, h.CodexBearer);

        using var response = await h.Client.SendAsync(
            Post(h.BaseUrl, h.CodexBearer, ToolsListBody, sessionId: session), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var names = ToolNames(await BodyJson(response));
        // codex gets the All profile: dialogue tools present, delete withheld.
        Assert.Contains("dialogue_create", names);
        Assert.DoesNotContain("delete", names);
    }

    [Fact]
    public async Task HomeSessionListsOnlyHomeToolsWithoutTouchingItsAliasOrSchema()
    {
        await using var h = Harness.Start();
        string session = await OpenSession(h, h.HomeBearer);

        using var response = await h.Client.SendAsync(
            Post(h.BaseUrl, h.HomeBearer, ToolsListBody, sessionId: session), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new[] { "create", "delete", "get", "search", "update" },
            ToolNames(await BodyJson(response)).OrderBy(value => value));
    }

    // ── session routing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NonInitializePostWithoutSessionHeaderIs400()
    {
        await using var h = Harness.Start();

        using var response = await h.Client.SendAsync(Post(h.BaseUrl, h.ClaudeBearer, ToolsListBody), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NonInitializePostWithUnknownSessionIs404()
    {
        await using var h = Harness.Start();

        using var response = await h.Client.SendAsync(
            Post(h.BaseUrl, h.ClaudeBearer, ToolsListBody, sessionId: "deadbeef"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ForeignBearerOnAnotherClientsSessionIs403()
    {
        await using var h = Harness.Start();
        // Session opened by claude; codex must not be able to drive it under its own bearer.
        string claudeSession = await OpenSession(h, h.ClaudeBearer);

        using var response = await h.Client.SendAsync(
            Post(h.BaseUrl, h.CodexBearer, ToolsListBody, sessionId: claudeSession), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── notifications and the JSON-RPC channel ──────────────────────────────────

    [Fact]
    public async Task NotificationDrawsA202WithEmptyBody()
    {
        await using var h = Harness.Start();
        string session = await OpenSession(h, h.ClaudeBearer);

        using var response = await h.Client.SendAsync(Post(
            h.BaseUrl, h.ClaudeBearer,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            sessionId: session), Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task ArrayBodyIsA200CarryingInvalidRequest()
    {
        await using var h = Harness.Start();

        // A batch (JSON array) is a JSON-RPC-level refusal: the request was delivered,
        // so the transport answers 200 and the failure is in the payload (-32600).
        using var response = await h.Client.SendAsync(
            Post(h.BaseUrl, h.ClaudeBearer, """[{"jsonrpc":"2.0","id":1,"method":"ping"}]"""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject error = (JsonObject)(await BodyJson(response))["error"]!;
        Assert.Equal(-32600, error["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task GarbageBodyIsA200CarryingParseError()
    {
        await using var h = Harness.Start();

        using var response = await h.Client.SendAsync(
            Post(h.BaseUrl, h.ClaudeBearer, "}{ not json"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject error = (JsonObject)(await BodyJson(response))["error"]!;
        Assert.Equal(-32700, error["code"]!.GetValue<int>());
    }

    // ── method allow-list and path ──────────────────────────────────────────────

    [Fact]
    public async Task GetIs405()
    {
        await using var h = Harness.Start();

        using var response = await h.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, h.BaseUrl), Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task PostToAnotherPathIs404()
    {
        await using var h = Harness.Start();
        // A path that is not the MCP endpoint: the listener answers 404 before anything else.
        string wrongPath = h.BaseUrl.Replace(McpHttpHost.EndpointPath, "/nope");

        using var response = await h.Client.SendAsync(Post(wrongPath, h.ClaudeBearer, InitializeBody()), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── session teardown ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTearsDownTheSessionSoAFollowUpIs404()
    {
        await using var h = Harness.Start();
        string session = await OpenSession(h, h.ClaudeBearer);

        var delete = new HttpRequestMessage(HttpMethod.Delete, h.BaseUrl);
        delete.Headers.TryAddWithoutValidation("Authorization", $"Bearer {h.ClaudeBearer}");
        delete.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        using var deleteResponse = await h.Client.SendAsync(delete, Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // The id is now forgotten: a follow-up on it is a 404, the server having torn it down.
        using var followUp = await h.Client.SendAsync(
            Post(h.BaseUrl, h.ClaudeBearer, ToolsListBody, sessionId: session), Ct);
        Assert.Equal(HttpStatusCode.NotFound, followUp.StatusCode);
    }
}
