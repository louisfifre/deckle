using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deckle.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Deckle.Anytype.Mcp;

// Bridges Deckle's catalog-owned descriptors to the official MCP protocol
// handlers. A fresh instance of the Deckle surface has already been opened for
// the authenticated HTTP request, so no identity or mutable transport session
// leaks into this adapter.
internal static class McpSurfaceProtocolAdapter
{
    private const string ServerVersion = "0.1.0";

    private sealed record PreparedTool(
        ToolDescriptor Descriptor,
        McpJsonSchemaContract Input,
        McpJsonSchemaContract? Output);

    public static void Configure(McpServerOptions options, McpSurfaceBinding surface)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(surface);

        var tools = new Dictionary<string, PreparedTool>(StringComparer.Ordinal);
        foreach (ToolDescriptor tool in surface.Tools)
        {
            tools.Add(
                tool.Name,
                new PreparedTool(
                    tool,
                    new McpJsonSchemaContract(tool.InputSchema),
                    tool.OutputSchema is null
                        ? null
                        : new McpJsonSchemaContract(tool.OutputSchema)));
        }

        options.ServerInfo = new Implementation
        {
            Name = surface.Descriptor.Name,
            Title = surface.Descriptor.Title,
            Version = ServerVersion,
        };
        options.ServerInstructions = surface.Descriptor.Instructions;
        options.Handlers.ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
        {
            Tools = surface.Tools.Select(ToProtocolTool).ToList(),
            TimeToLive = TimeSpan.Zero,
            CacheScope = CacheScope.Private,
        });
        string surfaceName = surface.Descriptor.Name;
        options.Handlers.CallToolHandler = (request, ct) => CallToolAsync(surfaceName, tools, request, ct);
    }

    private static Tool ToProtocolTool(ToolDescriptor descriptor)
    {
        var tool = new Tool
        {
            Name = descriptor.Name,
            Description = descriptor.Description,
            InputSchema = JsonSerializer.SerializeToElement(descriptor.InputSchema),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = descriptor.Execution.Effect == ToolEffect.ReadOnly,
                DestructiveHint = descriptor.Execution.Change is
                    ToolChangeKind.Overwriting or ToolChangeKind.Destructive,
                IdempotentHint = descriptor.Execution.AmbiguousOutcome == AmbiguousOutcomePolicy.SafeToRetry,
            },
            Meta = new JsonObject
            {
                ["deckle/ambiguousOutcome"] = AmbiguousOutcomeName(descriptor.Execution.AmbiguousOutcome),
                ["deckle/requiresStableTarget"] = descriptor.Execution.RequiresStableTarget,
            },
        };
        if (descriptor.OutputSchema is not null)
            tool.OutputSchema = JsonSerializer.SerializeToElement(descriptor.OutputSchema);
        return tool;
    }

    private static string AmbiguousOutcomeName(AmbiguousOutcomePolicy policy) => policy switch
    {
        AmbiguousOutcomePolicy.SafeToRetry => "safeToRetry",
        AmbiguousOutcomePolicy.VerifyBeforeRetry => "verifyBeforeRetry",
        AmbiguousOutcomePolicy.RequiresDeduplication => "requiresDeduplication",
        AmbiguousOutcomePolicy.Uncertain => "uncertain",
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null),
    };

    private static async ValueTask<CallToolResult> CallToolAsync(
        string surfaceName,
        IReadOnlyDictionary<string, PreparedTool> tools,
        RequestContext<CallToolRequestParams> request,
        CancellationToken ct)
    {
        long started = Stopwatch.GetTimestamp();
        string workId = WorkId.Next();
        string? name = request.Params?.Name;
        PreparedTool? tool = null;
        // An unknown name is caller-supplied text, so it is logged as
        // "unknown", never echoed; a known name is a catalog constant.
        string toolName = name is null
            ? ""
            : tools.TryGetValue(name, out tool) ? tool.Descriptor.Name : "unknown";
        string outcome = "failed";
        string exType = "";
        int statusCode = 0;
        string errorCode = "none";
        DeckleAnytypeMcpSource.Log.ToolCallStarted(surfaceName, toolName, workId);
        try
        {
            if (name is null)
            {
                outcome = "missing_tool_name";
                throw new McpProtocolException("Missing tool name", McpErrorCode.InvalidParams);
            }
            if (tool is null)
            {
                outcome = "unknown_tool";
                throw new McpProtocolException($"Unknown tool: {name}", McpErrorCode.InvalidParams);
            }

            JsonObject? arguments = ToJsonObject(request.Params?.Arguments);
            if (!tool.Input.Accepts(arguments ?? new JsonObject()))
            {
                outcome = "input_rejected";
                return ToolResult(
                    new ToolOutput("Tool input does not match the advertised JSON Schema."),
                    isError: true);
            }

            try
            {
                ToolOutput output = await tool.Descriptor.Handler(arguments, ct).ConfigureAwait(false);
                if (tool.Output is not null
                    && (output.StructuredContent is null || !tool.Output.Accepts(output.StructuredContent)))
                {
                    outcome = "output_rejected";
                    return ToolResult(
                        new ToolOutput("Tool output does not match the advertised JSON Schema."),
                        isError: true);
                }
                outcome = "completed";
                return ToolResult(output, isError: false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                outcome = "cancelled";
                throw;
            }
            catch (Exception ex)
            {
                // Catalog validation and execution errors are model-correctable tool
                // results. Only a missing or unknown tool name is a JSON-RPC
                // InvalidParams error.
                outcome = "failed";
                exType = ex.GetType().Name;
                // The message goes to the client in the tool result; the log gets
                // the type and, for an API refusal, the status and machine code.
                if (ex is AnytypeV2Exception refusal)
                {
                    statusCode = refusal.Status;
                    errorCode = refusal.LogCode;
                }
                return ToolResult(new ToolOutput(ex.Message), isError: true);
            }
        }
        catch (Exception ex) when (outcome == "failed")
        {
            // Anything thrown before the handler ran (argument conversion).
            exType = ex.GetType().Name;
            throw;
        }
        finally
        {
            double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (outcome is "completed" or "cancelled")
            {
                DeckleAnytypeMcpSource.Log.ToolCallCompleted(
                    surfaceName, toolName, outcome, exType, elapsedMs, workId);
            }
            else
            {
                DeckleAnytypeMcpSource.Log.ToolCallFailed(
                    surfaceName, toolName, outcome, exType, statusCode, errorCode, elapsedMs, workId);
            }
        }
    }

    private static JsonObject? ToJsonObject(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
            return null;

        var result = new JsonObject();
        foreach ((string name, JsonElement value) in arguments)
            result[name] = JsonNode.Parse(value.GetRawText());
        return result;
    }

    // The text goes out whole, on purpose. A former 8 000-character cap kept
    // the head and dropped the tail, so a report journal lost its newest
    // entries, the very ones a resumed session needs. Context cost is the
    // caller's choice of read (a whole object or a part of it), never a
    // silent cut here.
    private static CallToolResult ToolResult(ToolOutput output, bool isError) => new()
    {
        Content = [new TextContentBlock { Text = output.Text }],
        StructuredContent = output.StructuredContent is null
            ? null
            : JsonSerializer.SerializeToElement(output.StructuredContent),
        IsError = isError ? true : null,
    };
}
