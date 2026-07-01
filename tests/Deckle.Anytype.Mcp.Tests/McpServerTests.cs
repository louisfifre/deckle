using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// Unit tests for the transport-agnostic MCP dispatcher, driven by handing a
// parsed JSON-RPC message straight to HandleAsync and reading back the response
// object. FAKE tool descriptors (one echo tool, one throwing tool) stand in for
// the real gestures — no HTTP, no network, no framing. These pin the JSON-RPC
// method semantics the server owns: version negotiation, the pre-initialize
// gate, the two tools/call error channels, and the notification/malformed
// silence. Wire framing (line reading, HTTP bodies, session routing) now lives
// in the transport and is exercised by McpHttpHostTests.
[Trait("Category", "unit")]
public class McpServerTests
{
    const string ThrowMessage = "tool exploded";

    // The latest protocol revision this host speaks — a fallback target, so it is
    // itself the contract when an unknown version is negotiated down.
    const string LatestProtocol = "2025-11-25";

    // Echo returns the "value" argument; the throwing tool fails on every call.
    static IReadOnlyList<ToolDescriptor> FakeTools()
    {
        var schema = new JsonObject { ["type"] = "object", ["additionalProperties"] = false };
        return new ToolDescriptor[]
        {
            new("echo", "Echoes its value argument.", (JsonObject)schema.DeepClone(),
                (args, _) => Task.FromResult(args?["value"]?.GetValue<string>() ?? "")),
            new("boom", "Always throws.", (JsonObject)schema.DeepClone(),
                (_, _) => throw new InvalidOperationException(ThrowMessage)),
        };
    }

    static McpServer NewServer() => new(FakeTools());

    // Parse a request line into the JsonObject the dispatcher takes, then run one
    // HandleAsync pass. A null return means the server chose to stay silent.
    static JsonObject? Handle(McpServer server, string requestLine) =>
        server.HandleAsync((JsonObject)JsonNode.Parse(requestLine)!).GetAwaiter().GetResult();

    // Most tests need an already-initialized server: initialize opens the gate, so
    // a follow-up request is served rather than refused. Returns the same instance.
    static McpServer InitializedServer()
    {
        var server = NewServer();
        Handle(server, Initialize());
        return server;
    }

    static string Initialize(int id = 1, string version = LatestProtocol) =>
        $$$"""{"jsonrpc":"2.0","id":{{{id}}},"method":"initialize","params":{"protocolVersion":"{{{version}}}"}}""";

    // ── initialize / version negotiation ──────────────────────────────────────

    [Fact]
    public void InitializeEchoesAKnownProtocolVersion()
    {
        JsonObject? response = Handle(NewServer(), Initialize(version: "2025-06-18"));

        JsonObject result = (JsonObject)response!["result"]!;
        Assert.Equal("2025-06-18", result["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public void InitializeDowngradesAnUnknownProtocolVersionToLatest()
    {
        JsonObject? response = Handle(NewServer(), Initialize(version: "1999-01-01"));

        JsonObject result = (JsonObject)response!["result"]!;
        Assert.Equal(LatestProtocol, result["protocolVersion"]!.GetValue<string>());
    }

    // ── ping ──────────────────────────────────────────────────────────────────

    [Fact]
    public void PingAnswersEmptyObjectEvenBeforeInitialize()
    {
        // ping bypasses the gate: it must answer promptly whatever the state.
        JsonObject? response = Handle(NewServer(), """{"jsonrpc":"2.0","id":7,"method":"ping"}""");

        Assert.Equal(7, response!["id"]!.GetValue<int>());
        JsonObject result = (JsonObject)response["result"]!;
        Assert.Empty(result);
    }

    // ── pre-initialize gate ───────────────────────────────────────────────────

    [Fact]
    public void NonPingRequestBeforeInitializeIsRejectedWithInvalidRequest()
    {
        JsonObject? response = Handle(NewServer(), """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        JsonObject error = (JsonObject)response!["error"]!;
        Assert.Equal(-32600, error["code"]!.GetValue<int>());
    }

    // ── tools/list ────────────────────────────────────────────────────────────

    [Fact]
    public void ToolsListReturnsTheDescriptorsInOrder()
    {
        JsonObject? response = Handle(InitializedServer(), """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        var tools = (JsonArray)((JsonObject)response!["result"]!)["tools"]!;
        var names = tools.Select(t => ((JsonObject)t!)["name"]!.GetValue<string>()).ToArray();

        Assert.Equal(new[] { "echo", "boom" }, names);
    }

    // ── tools/call: the two error channels ────────────────────────────────────

    [Fact]
    public void ToolsCallOnAThrowingToolReturnsAnIsErrorResultNotAnRpcError()
    {
        JsonObject? response = Handle(InitializedServer(),
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"boom","arguments":{}}}""");

        // Execution failure is a normal result with isError:true, NOT a JSON-RPC error.
        Assert.False(response!.ContainsKey("error"));
        JsonObject result = (JsonObject)response["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        string text = ((JsonObject)((JsonArray)result["content"]!).Single()!)["text"]!.GetValue<string>();
        Assert.Equal(ThrowMessage, text);
    }

    [Fact]
    public void ToolsCallOnAnUnknownNameReturnsInvalidParamsRpcError()
    {
        JsonObject? response = Handle(InitializedServer(),
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");

        JsonObject error = (JsonObject)response!["error"]!;
        Assert.Equal(-32602, error["code"]!.GetValue<int>());
    }

    [Fact]
    public void ToolsCallOnTheEchoToolReturnsASuccessResult()
    {
        JsonObject? response = Handle(InitializedServer(),
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"echo","arguments":{"value":"salut"}}}""");

        JsonObject result = (JsonObject)response!["result"]!;
        Assert.False(result.ContainsKey("isError"));
        string text = ((JsonObject)((JsonArray)result["content"]!).Single()!)["text"]!.GetValue<string>();
        Assert.Equal("salut", text);
    }

    // ── notifications and malformed messages ──────────────────────────────────

    [Fact]
    public void ANotificationYieldsNoResponse()
    {
        // A message with no id is a notification: HandleAsync returns null, the
        // signal to the transport that there is nothing to write back.
        JsonObject? response = Handle(NewServer(), """{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Null(response);
    }

    [Fact]
    public void AMalformedNoIdMessageYieldsNoResponse()
    {
        // No method AND no id: the sender cannot be answered (no request slot to
        // key an error on), so the server stays silent rather than emit an error.
        JsonObject? response = Handle(NewServer(), """{"jsonrpc":"2.0"}""");

        Assert.Null(response);
    }

    [Fact]
    public void AMalformedRequestWithAnIdIsRejectedWithInvalidRequest()
    {
        // An id is present but no method: this request can be answered, so it earns
        // an -32600 rather than silence — the complement of the no-id case above.
        JsonObject? response = Handle(NewServer(), """{"jsonrpc":"2.0","id":9}""");

        JsonObject error = (JsonObject)response!["error"]!;
        Assert.Equal(-32600, error["code"]!.GetValue<int>());
    }
}
