using System.Text.Json.Nodes;

namespace Deckle.Anytype.Mcp;

// MCP dispatcher implementing the method semantics of revision 2025-11-25.
// Transport-agnostic: it takes a parsed JSON-RPC message and returns the
// response object, leaving wire framing (stdio lines, HTTP bodies) to the
// transport that owns the connection. One instance is session-scoped — the
// initialize gate lives here — so each MCP session holds its own server over
// its own gesture graph. Handling is per-message and synchronous-friendly:
// the underlying API client serialises calls anyway.
public sealed class McpServer
{
    private const string ServerVersion = "0.1.0";
    private const string LatestProtocol = "2025-11-25";

    // Protocol revisions this host speaks. A client asking for any of these
    // gets it echoed back; anything else falls back to the latest.
    private static readonly HashSet<string> SupportedProtocols =
        new(StringComparer.Ordinal) { "2025-03-26", "2025-06-18", "2025-11-25" };

    public sealed record Descriptor(string Name, string Title, string Instructions);

    public static readonly Descriptor ProjectManagementDescriptor = new(
        "deckle-anytype",
        "Deckle Anytype",
        "Anytype project-management space: projects (one per app or life area) "
        + "hold tasks; subtasks are inline '- [ ]' checklist items in the task "
        + "body, not separate objects. Before work that changes the space, call "
        + "session_start on the anchor task, then journal the why with log as "
        + "you go; plain reads need no session. Shared vocabulary — états: "
        + "termine, ouvert, en_cours, dormant, en_attente, abandonne; priority "
        + "0-5, 5 highest; content is French. Fill properties at creation: "
        + "date cible and définition de fini everywhere, plus estimated budget "
        + "and charge on projects — their 'réel' counterparts are set at "
        + "validation, so the estimate/actual delta stays readable. Select "
        + "options are applied, never created: new options come from the user, "
        + "in Anytype. Names "
        + "resolve to objects; an ambiguous name returns candidate ids so you "
        + "can retry with one.");

    public static readonly Descriptor DialoguesDescriptor = new(
        "deckle-anytype-dialogues",
        "Deckle Anytype Dialogues",
        "Anytype dialogue chats for mediated LLM discussions. Create a dialogue "
        + "chat for start, challenge, or dialogue work; post turns as system, "
        + "claude, codex, or louis; read the chat before each new turn and use "
        + "after_order_id to continue from the last seen message. These tools are "
        + "not project-management reports and do not journal work sessions.");

    public static readonly Descriptor SchemaAdminDescriptor = new(
        "deckle-anytype-schema-admin",
        "Deckle Anytype Schema Admin",
        "Anytype schema administration surface. It inspects configured space "
        + "aliases, previews additive type/property/tag changes, then applies a "
        + "previous preview only when confirm:true is passed. It never accepts a "
        + "raw space_id; use configured aliases such as dev or home. First scope "
        + "is additive only: no delete, key rename, property format change, or "
        + "property removal.");

    public static readonly Descriptor AllDescriptor = new(
        "deckle-anytype-all",
        "Deckle Anytype All",
        ProjectManagementDescriptor.Instructions + "\n\n" + DialoguesDescriptor.Instructions);

    private readonly Dictionary<string, ToolDescriptor> _tools;
    private readonly JsonArray _toolListing;
    private readonly Descriptor _descriptor;

    private bool _initialized;

    public McpServer(
        IReadOnlyList<ToolDescriptor> tools,
        Descriptor? descriptor = null)
    {
        _descriptor = descriptor ?? ProjectManagementDescriptor;
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

    // Dispatches one parsed message to its method handler and returns the
    // JSON-RPC response — a result or an error object — or null when the
    // message warrants no reply (a notification, or a malformed no-id message
    // whose sender cannot be answered). The transport writes the returned
    // object; a null means write nothing.
    public async Task<JsonObject?> HandleAsync(JsonObject message, CancellationToken ct = default)
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
            // A malformed message with no id cannot be answered — the request
            // slot it would key on does not exist; stay silent.
            return isNotification ? null : Error(id, -32600, "Invalid Request");
        }

        if (isNotification)
        {
            HandleNotification(method);
            return null;
        }

        return method switch
        {
            "initialize" => Initialize(id, message["params"] as JsonObject),
            // MUST respond promptly, even before initialize.
            "ping" => Result(id, new JsonObject()),
            "tools/list" => RejectIfUninitialized(id) ?? ToolsList(id),
            "tools/call" => RejectIfUninitialized(id)
                ?? await ToolsCallAsync(id, message["params"] as JsonObject, ct),
            _ => RejectIfUninitialized(id) ?? Error(id, -32601, $"Method not found: {method}"),
        };
    }

    // Body-level protocol error the transport raises before dispatch — a
    // -32700 on an unparseable body, a -32600 on a non-object one. Public so
    // the HTTP transport builds the same shape this class emits internally.
    public static JsonObject ProtocolError(JsonNode? id, int code, string message) =>
        Error(id, code, message);

    // Notifications are silent by contract. We only act on initialized to
    // open the gate; everything else (cancelled, anything unknown) is ignored.
    private void HandleNotification(string method)
    {
        if (method == "notifications/initialized")
            _initialized = true;
    }

    private JsonObject Initialize(JsonNode? id, JsonObject? @params)
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
                ["name"] = _descriptor.Name,
                ["title"] = _descriptor.Title,
                ["version"] = ServerVersion,
            },
            ["instructions"] = _descriptor.Instructions,
        };
        return Result(id, result);
    }

    private JsonObject ToolsList(JsonNode? id)
    {
        // All tools fit one page; cursor (if any) is accepted and ignored,
        // and no nextCursor is emitted.
        var result = new JsonObject { ["tools"] = _toolListing.DeepClone() };
        return Result(id, result);
    }

    private async Task<JsonObject> ToolsCallAsync(JsonNode? id, JsonObject? @params, CancellationToken ct)
    {
        string? name = AsString(@params?["name"]);
        if (name is null)
            return Error(id, -32602, "Missing tool name");

        if (!_tools.TryGetValue(name, out var tool))
        {
            // An unknown tool name is a protocol-level error (-32602): the
            // model called something that does not exist.
            return Error(id, -32602, $"Unknown tool: {name}");
        }

        var arguments = @params?["arguments"] as JsonObject;

        // Every execution failure — argument validation, NotFound, Ambiguous,
        // HTTP — comes back as a normal result with isError:true so the model
        // can self-correct. Only an unknown tool name reaches the error channel.
        try
        {
            string text = await tool.Handler(arguments, ct);
            return Result(id, ToolResult(text, isError: false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutdown, not a tool failure.
        }
        catch (Exception ex)
        {
            return Result(id, ToolResult(ex.Message, isError: true));
        }
    }

    // Response builders. An id node is owned by its inbound message, and a
    // JsonNode cannot have two parents, so clone before parenting it onto the
    // response — the wire-framing concern that moved in from the endpoint.
    private static JsonObject Result(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        },
    };

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

    // Pre-initialize gate. Returns an error response when the request must be
    // refused, or null to let the caller proceed; initialize and ping bypass
    // this and are never passed here.
    private JsonObject? RejectIfUninitialized(JsonNode? id) =>
        _initialized ? null : Error(id, -32600, "Server not initialized");
}
