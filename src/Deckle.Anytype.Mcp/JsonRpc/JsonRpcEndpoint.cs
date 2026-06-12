using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Deckle.Anytype.Mcp.JsonRpc;

// Transport framing for JSON-RPC 2.0 over stdio, MCP revision 2025-11-25:
// newline-delimited UTF-8 (no BOM), exactly one JSON object per line, no
// embedded newlines, no Content-Length headers, no batch arrays (removed
// in 2025-06-18 — a '['-leading message is rejected with -32600).
//
// stdout carries protocol messages only; the writer flushes after every
// line so the client never blocks waiting on a buffer. Reading and writing
// run on the same single-threaded loop (handlers are sequential), so no
// write lock is needed here.
public sealed class JsonRpcEndpoint
{
    private readonly TextReader _in;
    private readonly TextWriter _out;

    private static readonly JsonNodeOptions _nodeOptions = new() { PropertyNameCaseInsensitive = false };
    private static readonly JsonDocumentOptions _docOptions = new();

    public JsonRpcEndpoint(TextReader input, TextWriter output)
    {
        _in = input;
        _out = output;
    }

    public enum ReadStatus
    {
        // A well-formed JSON object is available in Message.
        Message,
        // stdin reached EOF — the caller exits cleanly.
        Eof,
        // A framing error was already answered on the wire (parse failure or
        // batch array); the caller loops to the next line.
        Handled,
    }

    public readonly record struct ReadResult(ReadStatus Status, JsonObject? Message);

    // Reads the next line and resolves it to one of three outcomes. Blank
    // lines are skipped silently (they are not messages). Parse failures and
    // batch arrays are answered here — framing is this layer's job — so the
    // dispatcher only ever sees a valid JsonObject.
    public async Task<ReadResult> ReadMessageAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            string? line = await _in.ReadLineAsync(ct);
            if (line is null)
                return new ReadResult(ReadStatus.Eof, null);

            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line, _nodeOptions, _docOptions);
            }
            catch (JsonException)
            {
                // Only place a null id is legitimate: the request id is
                // unknowable when the line did not parse.
                WriteError(null, -32700, "Parse error");
                return new ReadResult(ReadStatus.Handled, null);
            }

            if (node is JsonArray)
            {
                // Batch arrays were removed in 2025-06-18; reject as an
                // invalid request rather than processing element by element.
                WriteError(null, -32600, "Batch requests are not supported");
                return new ReadResult(ReadStatus.Handled, null);
            }

            if (node is JsonObject obj)
                return new ReadResult(ReadStatus.Message, obj);

            // A bare JSON scalar (number, string, true/false/null) is not a
            // request object.
            WriteError(null, -32600, "Invalid Request");
            return new ReadResult(ReadStatus.Handled, null);
        }
    }

    public void WriteResult(JsonNode? id, JsonNode result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneId(id),
            ["result"] = result,
        };
        WriteLine(response);
    }

    public void WriteError(JsonNode? id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneId(id),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        WriteLine(response);
    }

    // Serialise without indentation: one object, one line. WriteLine emits a
    // single '\n'-terminated record; the writer is configured for UTF-8
    // without BOM and AutoFlush by the host.
    private void WriteLine(JsonObject node)
    {
        _out.WriteLine(node.ToJsonString());
    }

    // An id node is owned by its inbound document; detach a copy before
    // parenting it onto the response object.
    private static JsonNode? CloneId(JsonNode? id) => id?.DeepClone();
}
