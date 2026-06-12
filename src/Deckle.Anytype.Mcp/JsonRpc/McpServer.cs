using System.Text.Json.Nodes;
using Deckle.Anytype.Mcp.Tools;

namespace Deckle.Anytype.Mcp.JsonRpc;

// MCP dispatcher implementing the stdio lifecycle of revision 2025-11-25.
// Single-threaded: messages are read and handled one at a time, which suits
// fast synchronous tools whose underlying API client serialises calls
// anyway. The endpoint owns wire framing; this class owns method dispatch,
// the initialize handshake, and the two tools/call error channels.
public sealed class McpServer
{
    private const string ServerName = "deckle-anytype";
    private const string ServerTitle = "Deckle Anytype";
    private const string ServerVersion = "0.1.0";
    private const string LatestProtocol = "2025-11-25";

    // Protocol revisions this host speaks. A client asking for any of these
    // gets it echoed back; anything else falls back to the latest.
    private static readonly HashSet<string> SupportedProtocols =
        new(StringComparer.Ordinal) { "2025-03-26", "2025-06-18", "2025-11-25" };

    private const string Instructions =
        "This server manages the user's Anytype project-management space "
        + "(projects -> tasks -> session reports). Start a work session with "
        + "session_start, then journal the why with log as you go. Names resolve "
        + "to objects; an ambiguous name returns candidate ids so you can retry "
        + "with one.";

    private readonly Dictionary<string, ToolDescriptor> _tools;
    private readonly JsonArray _toolListing;
    private readonly JsonRpcEndpoint _endpoint;

    private bool _initialized;

    public McpServer(IReadOnlyList<ToolDescriptor> tools, JsonRpcEndpoint endpoint)
    {
        _endpoint = endpoint;
        _tools = new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal);
        _toolListing = new JsonArray();
        foreach (var tool in tools)
        {
            _tools[tool.Name] = tool;
            _toolListing.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                // The schema is owned by the catalog; clone so the listing
                // node never reparents the descriptor's instance.
                ["inputSchema"] = tool.InputSchema.DeepClone(),
            });
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var read = await _endpoint.ReadMessageAsync(ct);
            switch (read.Status)
            {
                case JsonRpcEndpoint.ReadStatus.Eof:
                    return; // stdin closed → exit cleanly.
                case JsonRpcEndpoint.ReadStatus.Handled:
                    continue; // a framing error was already answered.
                case JsonRpcEndpoint.ReadStatus.Message:
                    await DispatchAsync(read.Message!, ct);
                    continue;
            }
        }
    }

    private async Task DispatchAsync(JsonObject message, CancellationToken ct)
    {
        // A message with no "id" member is a notification: it never receives
        // a response, whatever its method. (A present-but-null id is still a
        // request id slot, but the spec forbids null ids, so we treat only an
        // absent member as a notification.)
        bool isNotification = !message.ContainsKey("id");
        JsonNode? id = message.TryGetPropertyValue("id", out var idNode) ? idNode : null;
        string? method = AsString(message["method"]);

        if (method is null)
        {
            if (!isNotification)
                _endpoint.WriteError(id, -32600, "Invalid Request");
            return;
        }

        if (isNotification)
        {
            HandleNotification(method);
            return;
        }

        switch (method)
        {
            case "initialize":
                HandleInitialize(id, message["params"] as JsonObject);
                break;
            case "ping":
                // MUST respond promptly, even before initialize.
                _endpoint.WriteResult(id, new JsonObject());
                break;
            case "tools/list":
                if (RejectIfUninitialized(id)) break;
                HandleToolsList(id);
                break;
            case "tools/call":
                if (RejectIfUninitialized(id)) break;
                await HandleToolsCallAsync(id, message["params"] as JsonObject, ct);
                break;
            default:
                if (RejectIfUninitialized(id)) break;
                _endpoint.WriteError(id, -32601, $"Method not found: {method}");
                break;
        }
    }

    // Notifications are silent by contract. We only act on initialized to
    // open the gate; everything else (cancelled, anything unknown) is ignored.
    private void HandleNotification(string method)
    {
        if (method == "notifications/initialized")
            _initialized = true;
    }

    private void HandleInitialize(JsonNode? id, JsonObject? @params)
    {
        string requested = AsString(@params?["protocolVersion"]) ?? string.Empty;
        string negotiated = SupportedProtocols.Contains(requested) ? requested : LatestProtocol;

        // The handshake is complete once we answer initialize; the client's
        // notifications/initialized merely confirms. Open the gate now so a
        // client that pipelines tools/list right after initialize is served.
        _initialized = true;

        var result = new JsonObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JsonObject
            {
                // Static tool set: no listChanged, no list_changed notification.
                ["tools"] = new JsonObject(),
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["title"] = ServerTitle,
                ["version"] = ServerVersion,
            },
            ["instructions"] = Instructions,
        };
        _endpoint.WriteResult(id, result);
    }

    private void HandleToolsList(JsonNode? id)
    {
        // All tools fit one page; cursor (if any) is accepted and ignored,
        // and no nextCursor is emitted.
        var result = new JsonObject { ["tools"] = _toolListing.DeepClone() };
        _endpoint.WriteResult(id, result);
    }

    private async Task HandleToolsCallAsync(JsonNode? id, JsonObject? @params, CancellationToken ct)
    {
        string? name = AsString(@params?["name"]);
        if (name is null)
        {
            _endpoint.WriteError(id, -32602, "Missing tool name");
            return;
        }

        if (!_tools.TryGetValue(name, out var tool))
        {
            // An unknown tool name is a protocol-level error (-32602): the
            // model called something that does not exist.
            _endpoint.WriteError(id, -32602, $"Unknown tool: {name}");
            return;
        }

        var arguments = @params?["arguments"] as JsonObject;

        // Every execution failure — argument validation, NotFound, Ambiguous,
        // HTTP — comes back as a normal result with isError:true so the model
        // can self-correct. Only an unknown tool name reaches the error channel.
        try
        {
            string text = await tool.Handler(arguments, ct);
            _endpoint.WriteResult(id, ToolResult(text, isError: false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutdown, not a tool failure.
        }
        catch (Exception ex)
        {
            _endpoint.WriteResult(id, ToolResult(ex.Message, isError: true));
        }
    }

    private static JsonObject ToolResult(string text, bool isError)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = text },
            },
        };
        if (isError) result["isError"] = true;
        return result;
    }

    // Reads a node as a string without throwing on a non-string kind: a
    // method or tool name that arrives as a number or object simply yields
    // null, routed downstream to the right error rather than an uncaught
    // InvalidOperationException.
    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    // Pre-initialize gate. Returns true (and answers) when the request must be
    // refused; initialize and ping bypass this and are never passed here.
    private bool RejectIfUninitialized(JsonNode? id)
    {
        if (_initialized) return false;
        _endpoint.WriteError(id, -32600, "Server not initialized");
        return true;
    }
}
