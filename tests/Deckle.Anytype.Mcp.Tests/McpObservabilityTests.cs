using System.Diagnostics.Tracing;
using System.Net;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// What the MCP host leaves in the log: how each tool call ended and why, the
// clients whose token is missing, a start that was refused and how often a
// framework problem kept happening. The tests share one process-wide provider, so
// they run one at a time (AssemblyInfo.cs).
[Trait("Category", "observability")]
public sealed class McpObservabilityTests
{
    private const string Provider = "Deckle-AnytypeMcp";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    private static bool TravelsInTheJournal(EventWrittenEventArgs e)
        => e.Level <= EventLevel.Warning || (e.Tags & ObservationTags.Journal) != 0;

    private static AnytypeApiClient Api() => new(new AnytypeCredentials(
        "http://127.0.0.1:1", "2025-11-08", "dummy-key", "dummy-space"));

    private static readonly McpClientProfile ProbeClient = new(
        "probe",
        new McpSurface(
            "probe",
            _ => new McpSurfaceBinding(
                [
                    new ToolDescriptor(
                        "fine",
                        "Succeeds.",
                        new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
                        (_, _) => Task.FromResult("done"),
                        ToolExecutionContract.ReadOnly),
                    new ToolDescriptor(
                        "refused",
                        "Fails the way an Anytype API refusal does.",
                        new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
                        (_, _) => Task.FromException<string>(new AnytypeV2Exception(
                            "The API refused the note of Alice",
                            HttpStatusCode.Forbidden,
                            "forbidden_by_policy",
                            "Alice may not read this",
                            "ask Alice",
                            [])),
                        ToolExecutionContract.ReadOnly),
                    new ToolDescriptor(
                        "plain",
                        "Fails with a message that quotes content.",
                        new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
                        (_, _) => Task.FromException<string>(
                            new InvalidOperationException("The title of Alice's note")),
                        ToolExecutionContract.ReadOnly),
                ],
                new McpSurfaceDescriptor("probe", "Probe", "Test surface."))),
        "mcp-token-probe",
        "DECKLE_MCP_TOKEN_PROBE");

    // A host over the probe surface alone, on the first free loopback port.
    private sealed class ProbeHost : IAsyncDisposable
    {
        private readonly AnytypeApiClient _api;

        public McpHttpHost Host { get; }
        public string Bearer { get; }

        private ProbeHost(McpHttpHost host, AnytypeApiClient api, string bearer)
        {
            Host = host;
            _api = api;
            Bearer = bearer;
        }

        public static ProbeHost Start(AnytypeApiClient api)
        {
            var vault = new FakeSecretVault();
            var tokens = new McpClientTokens(vault, [ProbeClient]);
            tokens.EnsureMinted();
            Assert.True(vault.TryGet(ProbeClient.TokenSecretName, out string? bearer));

            for (int port = 34711; port < 34751; port++)
            {
                var host = new McpHttpHost(api, tokens, port);
                if (host.Start())
                    return new ProbeHost(host, api, bearer!);
                host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            throw new InvalidOperationException("No free loopback port found for the MCP test host.");
        }

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            _api.Dispose();
        }
    }

    private static async Task<McpClient> ConnectAsync(ProbeHost probe)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(probe.Host.BaseUrl),
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {probe.Bearer}",
            },
        });
        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions { ProtocolVersion = "2026-07-28" },
            cancellationToken: Ct);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AFailedToolCallReachesTheJournalWithItsTypeStatusAndCodeAndNeverItsMessage()
    {
        using var listener = new TestEventListener(Provider);
        await using var probe = ProbeHost.Start(Api());
        await using McpClient client = await ConnectAsync(probe);

        await client.CallToolAsync("fine", new Dictionary<string, object?>(), cancellationToken: Ct);
        CallToolResult refused = await client.CallToolAsync(
            "refused", new Dictionary<string, object?>(), cancellationToken: Ct);
        await client.CallToolAsync("plain", new Dictionary<string, object?>(), cancellationToken: Ct);

        // The client still gets the message; the log never does.
        Assert.True(refused.IsError);

        EventWrittenEventArgs[] failures = listener.Events
            .Where(e => e.EventId == DeckleAnytypeMcpSource.EvtToolCallFailed).ToArray();
        EventWrittenEventArgs api = Assert.Single(failures, e => Field(e, "tool") == "refused");
        Assert.Equal("failed", Field(api, "outcome"));
        Assert.Equal(nameof(AnytypeV2Exception), Field(api, "ex_type"));
        Assert.Equal("403", Field(api, "status_code"));
        Assert.Equal("forbidden_by_policy", Field(api, "error_code"));
        EventWrittenEventArgs plain = Assert.Single(failures, e => Field(e, "tool") == "plain");
        Assert.Equal(nameof(InvalidOperationException), Field(plain, "ex_type"));
        Assert.Equal("0", Field(plain, "status_code"));
        Assert.Equal("none", Field(plain, "error_code"));
        Assert.All(failures, e => Assert.True(TravelsInTheJournal(e)));

        EventWrittenEventArgs fine = Assert.Single(
            listener.Events,
            e => e.EventId == DeckleAnytypeMcpSource.EvtToolCallCompleted && Field(e, "tool") == "fine");
        Assert.Equal("completed", Field(fine, "outcome"));
        Assert.False(TravelsInTheJournal(fine));

        Assert.DoesNotContain(
            listener.Events,
            e => e.Payload!.Any(p => Convert.ToString(p)?.Contains("Alice") == true));
    }

    [Fact]
    public async Task EveryToolCallEndsWithTheWorkIdOfItsStart()
    {
        using var listener = new TestEventListener(Provider);
        await using var probe = ProbeHost.Start(Api());
        await using McpClient client = await ConnectAsync(probe);

        foreach (string tool in new[] { "fine", "refused" })
            await client.CallToolAsync(tool, new Dictionary<string, object?>(), cancellationToken: Ct);

        foreach (string tool in new[] { "fine", "refused" })
        {
            EventWrittenEventArgs started = Assert.Single(
                listener.Events,
                e => e.EventId == DeckleAnytypeMcpSource.EvtToolCallStarted && Field(e, "tool") == tool);
            EventWrittenEventArgs ended = Assert.Single(
                listener.Events,
                e => (e.EventId == DeckleAnytypeMcpSource.EvtToolCallCompleted
                        || e.EventId == DeckleAnytypeMcpSource.EvtToolCallFailed)
                    && Field(e, "tool") == tool);
            Assert.Equal(Field(started, WorkId.FieldName), Field(ended, WorkId.FieldName));
        }
    }

    [Fact]
    [Trait("Category", "regression")]
    public void ClientsWhoseTokenIsMissingFromTheVaultAreAWarningInTheJournal()
    {
        using var listener = new TestEventListener(Provider);
        // No EnsureMinted: every client is missing, so no environment variable is
        // written (the method writes real User variables when a token exists).
        var tokens = new McpClientTokens(new FakeSecretVault());

        tokens.MaterializeEnvironmentVariables();

        EventWrittenEventArgs missing = Assert.Single(
            listener.Events.Where(TravelsInTheJournal),
            e => e.EventId == DeckleAnytypeMcpSource.EvtTokenVariablesMissing);
        Assert.Equal(EventLevel.Warning, missing.Level);
        Assert.Equal(McpClients.All.Count().ToString(), Field(missing, "missing"));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task StartingADisposedHostSaysWhyItDidNotStart()
    {
        using var listener = new TestEventListener(Provider);
        using AnytypeApiClient api = Api();
        var host = new McpHttpHost(api, new McpClientTokens(new FakeSecretVault()), port: 34799);
        await host.DisposeAsync();

        bool started = host.Start();

        Assert.False(started);
        EventWrittenEventArgs refused = Assert.Single(
            listener.Events.Where(TravelsInTheJournal),
            e => e.EventId == DeckleAnytypeMcpSource.EvtHostStartRefused);
        Assert.Equal("disposed", Field(refused, "reason"));
    }

    private static ILogger BridgeLogger(McpHostLogBridge bridge) => bridge.CreateLogger("Kestrel");

    private static void Report(ILogger logger, string eventName, int times)
    {
        for (int i = 0; i < times; i++)
        {
            logger.Log(
                LogLevel.Warning,
                new EventId(7, eventName),
                "the request of Alice",
                new InvalidOperationException(),
                (state, _) => state);
        }
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AFrameworkProblemThatKeepsHappeningIsCountedInTheJournal()
    {
        using var listener = new TestEventListener(Provider);
        var bridge = new McpHostLogBridge();
        ILogger logger = BridgeLogger(bridge);

        Report(logger, "ConnectionReset", 25);
        bridge.Dispose();

        EventWrittenEventArgs[] journal = listener.Events.Where(TravelsInTheJournal).ToArray();
        // One Warning announces it; the count follows at 10, then at 25 when the host closes.
        Assert.Single(journal, e => e.EventId == DeckleAnytypeMcpSource.EvtHostInternalLog);
        Assert.Equal(
            ["10", "25"],
            journal.Where(e => e.EventId == DeckleAnytypeMcpSource.EvtHostInternalLogCounted)
                .Select(e => Field(e, "occurrences")));
        Assert.Equal(
            nameof(InvalidOperationException),
            Field(journal.Last(e => e.EventId == DeckleAnytypeMcpSource.EvtHostInternalLogCounted), "ex_type"));
        Assert.DoesNotContain(
            listener.Events,
            e => e.Payload!.Any(p => Convert.ToString(p)?.Contains("Alice") == true));
    }

    [Fact]
    public void AProblemThatHappenedOnceNeedsNoCountWhenTheHostCloses()
    {
        using var listener = new TestEventListener(Provider);
        var bridge = new McpHostLogBridge();

        Report(BridgeLogger(bridge), "ConnectionReset", 1);
        bridge.Dispose();

        Assert.DoesNotContain(
            listener.Events, e => e.EventId == DeckleAnytypeMcpSource.EvtHostInternalLogCounted);
    }
}
