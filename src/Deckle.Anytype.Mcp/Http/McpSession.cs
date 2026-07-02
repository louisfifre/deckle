using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// One MCP session over the HTTP transport: the state a single initialize opens and
// a matching DELETE (or idle eviction) tears down. A session pins the client whose
// bearer opened it and carries its own McpServer, built once for that client's
// surface so the tool profile and management gating cost nothing per request.
//
// The session id routes, it does not authenticate. It rides the Mcp-Session-Id
// header to steer a request back to the right server, but the bearer is re-checked
// on every request — a leaked id buys nothing without the token that opened it. The
// semaphore serialises HandleAsync within the session so a client that pipelines two
// requests under one id sees them dispatched in order, which the read-modify-write
// gestures rely on; the underlying AnytypeApiClient serialises across sessions
// anyway, so this only orders a single client's own traffic.
public sealed class McpSession
{
    private readonly McpServer _server;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Id { get; }
    public McpClientProfile Client { get; }
    public DateTimeOffset LastActivity { get; private set; }

    public McpSession(McpClientProfile client, AnytypeApiClient api)
    {
        Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        Client = client;

        var (tools, descriptor) = McpToolset.Build(api, client.Profile, client.Management);
        _server = new McpServer(tools, descriptor);
        LastActivity = DateTimeOffset.UtcNow;
    }

    // Dispatch one message under the session's serialising gate. Every entry stamps
    // the activity clock so idle eviction measures time since the last real request,
    // not since the session opened.
    public async Task<JsonObject?> HandleAsync(JsonObject message, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LastActivity = DateTimeOffset.UtcNow;
            return await _server.HandleAsync(message, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
