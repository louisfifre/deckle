using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp.JsonRpc;
using Deckle.Anytype.Mcp.Tools;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// Unit tests for the MCP dispatcher + framing, driven over in-memory streams with
// FAKE tool descriptors (one echo tool, one throwing tool). No real gestures, no
// HTTP: these pin the JSON-RPC lifecycle — version negotiation, the
// pre-initialize gate, the two tools/call error channels, notification silence,
// and the framing rejections the endpoint owns.
[Trait("Category", "unit")]
public class McpServerTests
{
    const string ThrowMessage = "tool exploded";

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

    // Feeds the joined request lines through one RunAsync pass (StringReader hits
    // EOF → the loop exits), then returns each response line parsed as JSON.
    static IReadOnlyList<JsonObject> Run(params string[] requestLines)
    {
        var input = new StringReader(string.Join("\n", requestLines) + "\n");
        var output = new StringWriter();
        var endpoint = new JsonRpcEndpoint(input, output);
        var server = new McpServer(FakeTools(), endpoint);

        server.RunAsync().GetAwaiter().GetResult();

        return output.ToString()
            .Split('\n', '\r')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => (JsonObject)JsonNode.Parse(l)!)
            .ToList();
    }

    static string Initialize(int id = 1, string version = "2025-11-25") =>
        $$$"""{"jsonrpc":"2.0","id":{{{id}}},"method":"initialize","params":{"protocolVersion":"{{{version}}}"}}""";

    // ── initialize / version negotiation ──────────────────────────────────────

    [Fact]
    public void InitializeEchoesAKnownProtocolVersion()
    {
        var responses = Run(Initialize(version: "2025-06-18"));

        JsonObject result = (JsonObject)responses.Single()["result"]!;
        Assert.Equal("2025-06-18", result["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public void InitializeDowngradesAnUnknownProtocolVersionToLatest()
    {
        var responses = Run(Initialize(version: "1999-01-01"));

        JsonObject result = (JsonObject)responses.Single()["result"]!;
        Assert.Equal("2025-11-25", result["protocolVersion"]!.GetValue<string>());
    }

    // ── ping ──────────────────────────────────────────────────────────────────

    [Fact]
    public void PingAnswersEmptyObjectEvenBeforeInitialize()
    {
        var responses = Run("""{"jsonrpc":"2.0","id":7,"method":"ping"}""");

        JsonObject response = responses.Single();
        Assert.Equal(7, response["id"]!.GetValue<int>());
        JsonObject result = (JsonObject)response["result"]!;
        Assert.Empty(result);
    }

    // ── pre-initialize gate ───────────────────────────────────────────────────

    [Fact]
    public void NonPingRequestBeforeInitializeIsRejectedWithInvalidRequest()
    {
        var responses = Run("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        JsonObject error = (JsonObject)responses.Single()["error"]!;
        Assert.Equal(-32600, error["code"]!.GetValue<int>());
    }

    // ── tools/list ────────────────────────────────────────────────────────────

    [Fact]
    public void ToolsListReturnsTheDescriptors()
    {
        var responses = Run(
            Initialize(id: 1),
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        JsonObject listResponse = responses.Single(r => r["id"]?.GetValue<int>() == 2);
        var tools = (JsonArray)((JsonObject)listResponse["result"]!)["tools"]!;
        var names = tools.Select(t => ((JsonObject)t!)["name"]!.GetValue<string>()).ToArray();

        Assert.Equal(new[] { "echo", "boom" }, names);
    }

    // ── tools/call: the two error channels ────────────────────────────────────

    [Fact]
    public void ToolsCallOnAThrowingToolReturnsAnIsErrorResultNotAnRpcError()
    {
        var responses = Run(
            Initialize(id: 1),
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"boom","arguments":{}}}""");

        JsonObject response = responses.Single(r => r["id"]?.GetValue<int>() == 2);
        // Execution failure is a normal result with isError:true, NOT a JSON-RPC error.
        Assert.False(response.ContainsKey("error"));
        JsonObject result = (JsonObject)response["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        string text = ((JsonObject)((JsonArray)result["content"]!).Single()!)["text"]!.GetValue<string>();
        Assert.Equal(ThrowMessage, text);
    }

    [Fact]
    public void ToolsCallOnAnUnknownNameReturnsInvalidParamsRpcError()
    {
        var responses = Run(
            Initialize(id: 1),
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");

        JsonObject response = responses.Single(r => r["id"]?.GetValue<int>() == 2);
        JsonObject error = (JsonObject)response["error"]!;
        Assert.Equal(-32602, error["code"]!.GetValue<int>());
    }

    [Fact]
    public void ToolsCallOnTheEchoToolReturnsASuccessResult()
    {
        var responses = Run(
            Initialize(id: 1),
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"echo","arguments":{"value":"salut"}}}""");

        JsonObject response = responses.Single(r => r["id"]?.GetValue<int>() == 2);
        JsonObject result = (JsonObject)response["result"]!;
        Assert.False(result.ContainsKey("isError"));
        string text = ((JsonObject)((JsonArray)result["content"]!).Single()!)["text"]!.GetValue<string>();
        Assert.Equal("salut", text);
    }

    // ── notifications and framing ─────────────────────────────────────────────

    [Fact]
    public void ANotificationNeverProducesAnOutputLine()
    {
        // notifications/initialized has no id → it is a notification → no response.
        var responses = Run("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Empty(responses);
    }

    [Fact]
    public void ABatchLeadingArrayIsRejectedWithInvalidRequest()
    {
        // Batch arrays were removed in 2025-06-18; a '['-leading line is rejected
        // with -32600 and a null id (the framing layer answers before dispatch).
        var responses = Run("""[{"jsonrpc":"2.0","id":1,"method":"ping"}]""");

        JsonObject response = responses.Single();
        JsonObject error = (JsonObject)response["error"]!;
        Assert.Equal(-32600, error["code"]!.GetValue<int>());
        Assert.True(response["id"] is null);
    }
}
