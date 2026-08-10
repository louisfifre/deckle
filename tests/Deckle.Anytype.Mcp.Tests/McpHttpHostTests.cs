using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// Public transport contracts over a real loopback Kestrel listener. Protocol
// behavior is driven through the official SDK client; raw HTTP is reserved for
// Deckle's own boundary (path, method, Origin, bearer and body limit).
[Trait("Category", "unit")]
public class McpHttpHostTests
{
    private const string ModernProtocol = "2026-07-28";
    private const string DownLevelProtocol = "2025-11-25";
    private const string LegacyJuneProtocol = "2025-06-18";
    private const string LegacyMarchProtocol = "2025-03-26";

    private static readonly McpClientProfile CustomClient = new(
        "custom",
        new McpSurface(
            "custom-dialogues",
            api => McpToolset.Build(api, ToolProfile.Dialogues, management: false)),
        "mcp-token-custom",
        "DECKLE_MCP_TOKEN_CUSTOM");

    private static readonly McpClientProfile StructuredClient = new(
        "structured",
        new McpSurface(
            "structured",
            _ => new McpSurfaceBinding(
                [
                    new ToolDescriptor(
                        "inspect",
                        "Returns a schema-backed inspection result.",
                        new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["mode"] = new JsonObject
                                {
                                    ["type"] = "string",
                                    ["enum"] = new JsonArray { "safe" },
                                },
                                ["count"] = new JsonObject
                                {
                                    ["type"] = "integer",
                                    ["minimum"] = 1,
                                    ["maximum"] = 2,
                                },
                            },
                            ["additionalProperties"] = false,
                        },
                        (_, _) => Task.FromResult(new ToolOutput(
                            "Inspection complete.",
                            new JsonObject { ["status"] = "complete", ["count"] = 2 })),
                        ToolExecutionContract.ReadOnly)
                    {
                        OutputSchema = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["status"] = new JsonObject { ["type"] = "string" },
                                ["count"] = new JsonObject { ["type"] = "integer" },
                            },
                            ["required"] = new JsonArray { "status", "count" },
                            ["additionalProperties"] = false,
                        },
                    },
                    new ToolDescriptor(
                        "broken_output",
                        "Returns structured content that violates its schema.",
                        new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
                        (_, _) => Task.FromResult(new ToolOutput(
                            "Broken output.",
                            new JsonObject { ["status"] = 7 })),
                        ToolExecutionContract.ReadOnly)
                    {
                        OutputSchema = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["status"] = new JsonObject { ["type"] = "string" },
                            },
                            ["required"] = new JsonArray { "status" },
                            ["additionalProperties"] = false,
                        },
                    },
                    new ToolDescriptor(
                        "boom",
                        "Always fails with a model-facing tool error.",
                        new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
                        (_, _) => Task.FromException<string>(new McpException("tool exploded")),
                        ToolExecutionContract.ReadOnly),
                ],
                new McpSurfaceDescriptor("structured", "Structured", "Test surface."))),
        "mcp-token-structured",
        "DECKLE_MCP_TOKEN_STRUCTURED");

    private sealed class Harness : IAsyncDisposable
    {
        private readonly AnytypeApiClient _api;
        private readonly McpClientTokens _tokens;
        private readonly McpRequestRateLimit _requestRateLimit;
        private bool _disposed;

        public McpHttpHost Host { get; private set; }
        public HttpClient Http { get; } = new();
        public int Port { get; }
        public string BaseUrl => Host.BaseUrl;
        public IReadOnlyDictionary<string, string> Bearers { get; }

        private Harness(
            McpHttpHost host,
            AnytypeApiClient api,
            McpClientTokens tokens,
            int port,
            IReadOnlyDictionary<string, string> bearers,
            McpRequestRateLimit requestRateLimit)
        {
            Host = host;
            _api = api;
            _tokens = tokens;
            _requestRateLimit = requestRateLimit;
            Port = port;
            Bearers = bearers;
        }

        public static Harness Start(
            McpClientProfile? additionalClient = null,
            McpRequestRateLimit? requestRateLimit = null)
        {
            requestRateLimit ??= McpRequestRateLimit.Default;
            McpClientProfile[] clients = additionalClient is null
                ? [.. McpClients.All]
                : [.. McpClients.All, additionalClient];
            var vault = new FakeSecretVault();
            var tokens = new McpClientTokens(vault, clients);
            tokens.EnsureMinted();

            var bearers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (McpClientProfile client in clients)
            {
                Assert.True(vault.TryGet(client.TokenSecretName, out string? bearer));
                bearers.Add(client.Id, bearer!);
            }

            var api = new AnytypeApiClient(new AnytypeCredentials(
                "http://127.0.0.1:1", "2025-11-08", "dummy-key", "dummy-space"));

            for (int port = 34611; port < 34651; port++)
            {
                var host = new McpHttpHost(api, tokens, port, requestRateLimit);
                if (host.Start())
                    return new Harness(host, api, tokens, port, bearers, requestRateLimit);
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            api.Dispose();
            throw new InvalidOperationException("No free loopback port found for the MCP test host.");
        }

        public async Task RestartAsync()
        {
            Assert.True(await Host.StopAsync(Ct));
            await Host.DisposeAsync();

            Host = new McpHttpHost(_api, _tokens, Port, _requestRateLimit);
            Assert.True(Host.Start());
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;
            Http.Dispose();
            await Host.DisposeAsync();
            _api.Dispose();
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpRequestMessage Post(
        string url,
        string? bearer,
        string body,
        string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (bearer is not null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
        if (origin is not null)
            request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }

    private static async Task<McpClient> ConnectAsync(
        Harness harness,
        string clientId,
        string protocolVersion)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(harness.BaseUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {harness.Bearers[clientId]}",
            },
        });
        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ProtocolVersion = protocolVersion },
            cancellationToken: Ct);
    }

    [Fact]
    public async Task RequestWithoutBearerIsChallengedWith401()
    {
        await using var harness = Harness.Start();

        using HttpResponseMessage response = await harness.Http.SendAsync(
            Post(harness.BaseUrl, bearer: null, "{}"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
    }

    [Fact]
    public async Task RequestWithUnknownBearerIs401()
    {
        await using var harness = Harness.Start();

        using HttpResponseMessage response = await harness.Http.SendAsync(
            Post(harness.BaseUrl, "not-a-real-token", "{}"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForeignOriginIs403WhileAbsentOriginPassesTheSecurityBoundary()
    {
        await using var harness = Harness.Start();
        string bearer = harness.Bearers[McpClients.Claude.Id];

        using HttpResponseMessage foreign = await harness.Http.SendAsync(
            Post(harness.BaseUrl, bearer, "{}", "http://evil.example"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using HttpResponseMessage absent = await harness.Http.SendAsync(
            Post(harness.BaseUrl, bearer, "{}"), Ct);
        Assert.NotEqual(HttpStatusCode.Forbidden, absent.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, absent.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task GetAndDeleteMethodsAre405(string method)
    {
        await using var harness = Harness.Start();
        using var request = new HttpRequestMessage(new HttpMethod(method), harness.BaseUrl);

        using HttpResponseMessage response = await harness.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Allow", out IEnumerable<string>? allowed));
        Assert.Contains("POST", allowed);
    }

    [Fact]
    public async Task AnotherPathIs404BeforeAuthentication()
    {
        await using var harness = Harness.Start();
        string wrongPath = harness.BaseUrl.Replace(McpHttpHost.EndpointPath, "/nope");

        using HttpResponseMessage response = await harness.Http.SendAsync(
            Post(wrongPath, bearer: null, "{}"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequestBodyPastTheHostLimitIs413()
    {
        await using var harness = Harness.Start();
        string body = new('x', checked((int)McpHttpHost.MaxRequestBodyBytes + 1));

        using HttpResponseMessage response = await harness.Http.SendAsync(
            Post(harness.BaseUrl, harness.Bearers[McpClients.Claude.Id], body), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData(ModernProtocol)]
    [InlineData(DownLevelProtocol)]
    [InlineData(LegacyJuneProtocol)]
    [InlineData(LegacyMarchProtocol)]
    public async Task OfficialClientListsToolsAtCurrentAndDownLevelProtocols(string protocolVersion)
    {
        await using var harness = Harness.Start();
        await using McpClient client = await ConnectAsync(harness, McpClients.Claude.Id, protocolVersion);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal(protocolVersion, client.NegotiatedProtocolVersion);
        Assert.Contains(tools, tool => tool.Name == "create_task");
    }

    [Fact]
    public async Task EachBearerGetsOnlyItsConfiguredSurface()
    {
        await using var harness = Harness.Start(CustomClient);
        await using McpClient claude = await ConnectAsync(
            harness, McpClients.Claude.Id, ModernProtocol);
        await using McpClient codex = await ConnectAsync(
            harness, McpClients.Codex.Id, ModernProtocol);
        await using McpClient custom = await ConnectAsync(
            harness, CustomClient.Id, ModernProtocol);

        string[] claudeTools = (await claude.ListToolsAsync(cancellationToken: Ct))
            .Select(tool => tool.Name).ToArray();
        string[] codexTools = (await codex.ListToolsAsync(cancellationToken: Ct))
            .Select(tool => tool.Name).ToArray();
        string[] customTools = (await custom.ListToolsAsync(cancellationToken: Ct))
            .Select(tool => tool.Name).ToArray();

        Assert.Contains("delete", claudeTools);
        Assert.DoesNotContain("dialogue_create", claudeTools);
        Assert.Contains("dialogue_create", codexTools);
        Assert.DoesNotContain("delete", codexTools);
        Assert.Contains("dialogue_create", customTools);
        Assert.DoesNotContain("create_task", customTools);
    }

    [Fact]
    public async Task ExecutionContractsAreProjectedIntoProtocolAnnotationsAndMetadata()
    {
        await using var harness = Harness.Start();
        await using McpClient client = await ConnectAsync(
            harness, McpClients.Claude.Id, ModernProtocol);
        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Ct);

        Tool read = tools.Single(tool => tool.Name == "get").ProtocolTool;
        Assert.True(read.Annotations!.ReadOnlyHint);
        Assert.False(read.Annotations.DestructiveHint);
        Assert.True(read.Annotations.IdempotentHint);
        Assert.Equal("safeToRetry", read.Meta!["deckle/ambiguousOutcome"]!.GetValue<string>());

        Tool create = tools.Single(tool => tool.Name == "create_task").ProtocolTool;
        Assert.False(create.Annotations!.ReadOnlyHint);
        Assert.False(create.Annotations.DestructiveHint);
        Assert.False(create.Annotations.IdempotentHint);
        Assert.Equal("requiresDeduplication",
            create.Meta!["deckle/ambiguousOutcome"]!.GetValue<string>());

        Tool overwrite = tools.Single(tool => tool.Name == "complete").ProtocolTool;
        Assert.False(overwrite.Annotations!.ReadOnlyHint);
        Assert.True(overwrite.Annotations.DestructiveHint);
        Assert.True(overwrite.Annotations.IdempotentHint);

        Tool delete = tools.Single(tool => tool.Name == "delete").ProtocolTool;
        Assert.True(delete.Annotations!.DestructiveHint);
        Assert.True(delete.Meta!["deckle/requiresStableTarget"]!.GetValue<bool>());
    }

    [Fact]
    public async Task StructuredResultsKeepTheirTextFallbackAndAdvertisedSchema()
    {
        await using var harness = Harness.Start(StructuredClient);
        await using McpClient client = await ConnectAsync(
            harness, StructuredClient.Id, ModernProtocol);

        Tool tool = (await client.ListToolsAsync(cancellationToken: Ct))
            .Single(value => value.Name == "inspect").ProtocolTool;
        Assert.NotNull(tool.OutputSchema);

        CallToolResult result = await client.CallToolAsync(
            "inspect", new Dictionary<string, object?>(), cancellationToken: Ct);

        TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("Inspection complete.", text.Text);
        Assert.False(result.IsError);
        Assert.Equal("complete", result.StructuredContent!.Value
            .GetProperty("status").GetString());
        Assert.Equal(2, result.StructuredContent.Value.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ToolInputsAreValidatedAgainstTheirAdvertisedSchemaBeforeDispatch()
    {
        await using var harness = Harness.Start(StructuredClient);
        await using McpClient client = await ConnectAsync(
            harness, StructuredClient.Id, ModernProtocol);
        Dictionary<string, object?>[] invalidInputs =
        [
            new() { ["unexpected"] = true },
            new() { ["mode"] = "unsafe" },
            new() { ["count"] = 3 },
        ];

        foreach (Dictionary<string, object?> input in invalidInputs)
        {
            CallToolResult result = await client.CallToolAsync(
                "inspect", input, cancellationToken: Ct);

            Assert.True(result.IsError);
            TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
            Assert.Contains("input", text.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Null(result.StructuredContent);
        }
    }

    [Fact]
    public async Task InvalidStructuredOutputIsRefusedBeforeItCrossesTheProtocolBoundary()
    {
        await using var harness = Harness.Start(StructuredClient);
        await using McpClient client = await ConnectAsync(
            harness, StructuredClient.Id, ModernProtocol);

        CallToolResult result = await client.CallToolAsync(
            "broken_output", new Dictionary<string, object?>(), cancellationToken: Ct);

        Assert.True(result.IsError);
        TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Contains("output", text.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.StructuredContent);
    }

    [Fact]
    public async Task HandlerFailureIsReturnedAsAModelFacingToolError()
    {
        await using var harness = Harness.Start(StructuredClient);
        await using McpClient client = await ConnectAsync(
            harness, StructuredClient.Id, ModernProtocol);

        CallToolResult result = await client.CallToolAsync(
            "boom", new Dictionary<string, object?>(), cancellationToken: Ct);

        Assert.True(result.IsError);
        TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("tool exploded", text.Text);
    }

    [Fact]
    public async Task UnknownToolIsAnInvalidParamsProtocolError()
    {
        await using var harness = Harness.Start(StructuredClient);
        await using McpClient client = await ConnectAsync(
            harness, StructuredClient.Id, ModernProtocol);

        McpProtocolException error = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync(
                "missing", new Dictionary<string, object?>(), cancellationToken: Ct).AsTask());

        Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
    }

    [Fact]
    public async Task EachAuthenticatedBearerHasAnIndependentRequestLimit()
    {
        var requestLimit = new McpRequestRateLimit(permitLimit: 2, window: TimeSpan.FromHours(1));
        await using var harness = Harness.Start(requestRateLimit: requestLimit);
        string claudeBearer = harness.Bearers[McpClients.Claude.Id];

        for (int request = 0; request < requestLimit.PermitLimit + 1; request++)
        {
            using HttpResponseMessage unauthenticated = await harness.Http.SendAsync(
                Post(harness.BaseUrl, bearer: null, "{}"), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        }

        for (int request = 0; request < requestLimit.PermitLimit; request++)
        {
            using HttpResponseMessage accepted = await harness.Http.SendAsync(
                Post(harness.BaseUrl, claudeBearer, "{}"), Ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, accepted.StatusCode);
        }

        using HttpResponseMessage rejected = await harness.Http.SendAsync(
            Post(harness.BaseUrl, claudeBearer, "{}"), Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        using HttpResponseMessage independent = await harness.Http.SendAsync(
            Post(harness.BaseUrl, harness.Bearers[McpClients.Codex.Id], "{}"), Ct);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, independent.StatusCode);
    }

    [Theory]
    [InlineData(ModernProtocol)]
    [InlineData(DownLevelProtocol)]
    [InlineData(LegacyJuneProtocol)]
    [InlineData(LegacyMarchProtocol)]
    [Trait("Category", "regression")]
    public async Task FreshRequestWorksAtTheSameUrlAndBearerAfterHostRestart(string protocolVersion)
    {
        await using var harness = Harness.Start();

        await using (McpClient before = await ConnectAsync(
            harness, McpClients.Claude.Id, protocolVersion))
        {
            Assert.Contains(await before.ListToolsAsync(cancellationToken: Ct),
                tool => tool.Name == "create_task");
        }

        string url = harness.BaseUrl;
        string bearer = harness.Bearers[McpClients.Claude.Id];
        await harness.RestartAsync();

        Assert.Equal(url, harness.BaseUrl);
        Assert.Equal(bearer, harness.Bearers[McpClients.Claude.Id]);
        await using McpClient after = await ConnectAsync(
            harness, McpClients.Claude.Id, protocolVersion);
        Assert.Contains(await after.ListToolsAsync(cancellationToken: Ct),
            tool => tool.Name == "create_task");
    }

    [Fact]
    public async Task StopReportsDrainedOnlyAfterTheActiveToolCallCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        McpClientProfile blockingClient = BlockingClient(entered, release);

        await using var harness = Harness.Start(blockingClient);
        await using McpClient client = await ConnectAsync(
            harness, blockingClient.Id, ModernProtocol);
        Task<CallToolResult> call = client.CallToolAsync(
            "block", new Dictionary<string, object?>(), cancellationToken: Ct).AsTask();

        await entered.Task.WaitAsync(Ct);
        Task<bool> stop = harness.Host.StopAsync(Ct);
        try
        {
            Assert.False(stop.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }

        CallToolResult result = await call;
        Assert.True(await stop);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task StopReportsNotDrainedWhenTheShutdownBudgetIsCancelled()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        McpClientProfile blockingClient = BlockingClient(entered, release);

        await using var harness = Harness.Start(blockingClient);
        await using McpClient client = await ConnectAsync(
            harness, blockingClient.Id, ModernProtocol);
        Task<CallToolResult> call = client.CallToolAsync(
            "block", new Dictionary<string, object?>(), cancellationToken: Ct).AsTask();

        await entered.Task.WaitAsync(Ct);
        using var shutdown = new CancellationTokenSource();
        Task<bool> stop = harness.Host.StopAsync(shutdown.Token);
        try
        {
            Assert.False(stop.IsCompleted);
            shutdown.Cancel();
            Assert.False(await stop);
        }
        finally
        {
            release.TrySetResult();
        }

        try { await call; }
        catch (McpException) { }
        catch (OperationCanceledException) { }
    }

    private static McpClientProfile BlockingClient(
        TaskCompletionSource entered,
        TaskCompletionSource release) =>
        new(
            "blocking",
            new McpSurface("blocking", _ => new McpSurfaceBinding(
                [new ToolDescriptor(
                    "block",
                    "Waits until the test releases it.",
                    new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
                    async (_, ct) =>
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(ct);
                        return "done";
                    },
                    ToolExecutionContract.ReadOnly)],
                new McpSurfaceDescriptor("blocking", "Blocking", "Test surface."))),
            "mcp-token-blocking",
            "DECKLE_MCP_TOKEN_BLOCKING");
}
