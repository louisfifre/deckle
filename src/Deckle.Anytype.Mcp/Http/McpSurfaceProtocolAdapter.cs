using System.Text.Json;
using System.Text.Json.Nodes;
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
        options.Handlers.CallToolHandler = (request, ct) => CallToolAsync(tools, request, ct);
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
        IReadOnlyDictionary<string, PreparedTool> tools,
        RequestContext<CallToolRequestParams> request,
        CancellationToken ct)
    {
        string? name = request.Params?.Name;
        if (name is null)
            throw new McpProtocolException("Missing tool name", McpErrorCode.InvalidParams);
        if (!tools.TryGetValue(name, out PreparedTool? tool))
            throw new McpProtocolException($"Unknown tool: {name}", McpErrorCode.InvalidParams);

        JsonObject? arguments = ToJsonObject(request.Params?.Arguments);
        if (!tool.Input.Accepts(arguments ?? new JsonObject()))
        {
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
                return ToolResult(
                    new ToolOutput("Tool output does not match the advertised JSON Schema."),
                    isError: true);
            }
            return ToolResult(output, isError: false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Catalog validation and execution errors are model-correctable tool
            // results. Only an absent tool name is a JSON-RPC InvalidParams error.
            return ToolResult(new ToolOutput(ex.Message), isError: true);
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

    private static CallToolResult ToolResult(ToolOutput output, bool isError) => new()
    {
        Content = [new TextContentBlock { Text = output.Text }],
        StructuredContent = output.StructuredContent is null
            ? null
            : JsonSerializer.SerializeToElement(output.StructuredContent),
        IsError = isError ? true : null,
    };
}
