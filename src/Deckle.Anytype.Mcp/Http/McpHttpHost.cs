using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Anytype.Mcp;

// The resident streamable-HTTP MCP host (spec 2025-11-25). One HttpListener bound on
// loopback replaces the per-client stdio exes the old design spawned (ADR-0002): both
// callers now reach the same in-process server, told apart by their bearer. This is
// the single-JSON-response flavour of the transport — no SSE, no server-initiated
// stream — because every tool answers synchronously and a resident host has no reason
// to hold a stream open.
//
// The two channels of the protocol stay distinct. HTTP status codes are the
// transport's own voice, reserved for transport-level refusals: a bad path, a missing
// bearer, an unknown session. A request that reaches the JSON-RPC layer and fails
// there (a batch, a parse error) rides a 200 carrying a JSON-RPC error object — the
// message was delivered, the transport did its job, the failure is the payload's.
//
// Concurrency shape: one accept loop pulls contexts off the listener and hands each to
// a fire-and-forget task that catches everything, so a slow or throwing request never
// stalls the next accept. Within a session, HandleAsync is serialised (McpSession's
// semaphore); across sessions, the AnytypeApiClient serialises the actual API calls
// process-wide, so the host itself writes plain async code and leans on those two
// locks for ordering.
public sealed class McpHttpHost : IAsyncDisposable
{
    // Loopback, outside every neighbour's range: 3100x-3101x is Anytype, 11434 is
    // Ollama, and the Windows dynamic range opens at 49152. 33255 was verified
    // bindable unelevated on 127.0.0.1.
    public const int DefaultPort = 33255;

    public const string EndpointPath = "/mcp";

    // The single request-body cap. A legitimate tool call is kilobytes; anything past
    // 4 MB is a runaway or a probe, refused before it is buffered.
    private const int MaxBodyBytes = 4 * 1024 * 1024;

    // The header the transport routes on: it steers a follow-up request back to the
    // server its initialize opened. The sibling MCP-Protocol-Version header is left
    // deliberately unread — negotiation already happened in initialize, so enforcing it
    // here would only break otherwise-fine clients.
    private const string SessionHeader = "Mcp-Session-Id";

    // How long a session may sit untouched before a later session creation reaps it.
    // Sessions are cheap but not free, and a client that walked away should not pin one
    // forever; eviction on creation keeps the table self-cleaning without a timer.
    private static readonly TimeSpan SessionIdleLimit = TimeSpan.FromHours(24);

    private readonly AnytypeApiClient _api;
    private readonly McpClientTokens _tokens;
    private readonly int _port;

    private readonly ConcurrentDictionary<string, McpSession> _sessions = new(StringComparer.Ordinal);
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    // Every in-flight handler, tracked so disposal can drain them: the accept
    // loop never awaits its children, but the AnytypeApiClient the handlers use
    // is disposed by the app right after this host — tearing it down under a
    // running tool call would throw where nobody watches. Keyed by a completion
    // task registered BEFORE the handler starts, so a handler that finishes
    // instantly can never race its own registration.
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

    private Task? _acceptLoop;
    private bool _started;
    private bool _disposed;

    public McpHttpHost(AnytypeApiClient api, McpClientTokens tokens, int port = DefaultPort)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(tokens);
        _api = api;
        _tokens = tokens;
        _port = port;
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
    }

    public string BaseUrl => $"http://127.0.0.1:{_port}{EndpointPath}";

    // Bind and open the accept loop. Idempotent: a second call after a successful start
    // is a no-op returning true. A bind that throws (the port is taken, or a policy
    // denies the reservation) is a hard start failure — logged and reported false, not
    // retried, since nothing about waiting would free the port.
    public bool Start()
    {
        if (_started)
            return true;

        try
        {
            _listener.Start();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            DeckleAnytypeMcpSource.Log.RequestRejected();
            DeckleAnytypeMcpSource.Log.RequestRejectedDetail($"bind-failed: {ex.Message}", 0);
            return false;
        }

        _started = true;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));

        DeckleAnytypeMcpSource.Log.HostStarted();
        DeckleAnytypeMcpSource.Log.HostStartedDetail(BaseUrl);
        return true;
    }

    // Pull contexts off the listener until cancellation, handing each to its own
    // fire-and-forget task. The loop never awaits a handler, so one slow request cannot
    // hold up the next accept; every handler catches its own exceptions, so a throw
    // never escapes to the loop. The GetContextAsync that is pending when the listener
    // stops throws — that is the shutdown signal, swallowed here.
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return; // listener stopped/closed — the intended way out.
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight[completion.Task] = 0;
            // CancellationToken.None on the Task.Run itself: the lambda must run
            // even during shutdown, or its finally would never clear the
            // registration and the drain would wait on a handler that never was.
            // Cancellation still reaches the handler through the ct it captures.
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleContextAsync(context, ct).ConfigureAwait(false);
                }
                catch
                {
                    // A handler must never surface: it has already tried to answer, and a
                    // second failure here would only tear down the accept loop's child
                    // task. Close the response defensively so the socket is not leaked.
                    try { context.Response.Abort(); } catch { }
                }
                finally
                {
                    _inFlight.TryRemove(completion.Task, out _);
                    completion.SetResult();
                }
            }, CancellationToken.None);
        }
    }

    // ── Request handling ──────────────────────────────────────────────────

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken ct)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        // Wrong door: only the MCP endpoint exists on this listener.
        if (!string.Equals(request.Url?.AbsolutePath, EndpointPath, StringComparison.Ordinal))
        {
            await WriteStatusAsync(response, 404, ct).ConfigureAwait(false);
            return;
        }

        // No server-initiated stream, so GET has nothing to open; only POST and DELETE
        // carry meaning here.
        if (HttpMethods.IsGet(request.HttpMethod))
        {
            response.AddHeader("Allow", "POST, DELETE");
            await WriteStatusAsync(response, 405, ct).ConfigureAwait(false);
            return;
        }
        if (!HttpMethods.IsPost(request.HttpMethod) && !HttpMethods.IsDelete(request.HttpMethod))
        {
            response.AddHeader("Allow", "POST, DELETE");
            await WriteStatusAsync(response, 405, ct).ConfigureAwait(false);
            return;
        }

        // Origin, when present, must be local — the DNS-rebinding guard the spec calls
        // for. A non-browser client sends no Origin at all, which is fine; only a
        // present, foreign origin is refused.
        string? origin = request.Headers["Origin"];
        if (origin is not null && !IsLocalOrigin(origin))
        {
            DeckleAnytypeMcpSource.Log.RequestRejected();
            DeckleAnytypeMcpSource.Log.RequestRejectedDetail("origin", 403);
            await WriteStatusAsync(response, 403, ct).ConfigureAwait(false);
            return;
        }

        // The bearer is the identity, checked on every request whatever the method. A
        // missing or unknown token gets the standard 401 challenge; the presented token
        // is never logged.
        McpClientProfile? client = _tokens.Authenticate(ReadBearer(request));
        if (client is null)
        {
            response.AddHeader("WWW-Authenticate", "Bearer");
            DeckleAnytypeMcpSource.Log.RequestRejected();
            DeckleAnytypeMcpSource.Log.RequestRejectedDetail("auth", 401);
            await WriteStatusAsync(response, 401, ct).ConfigureAwait(false);
            return;
        }

        if (HttpMethods.IsDelete(request.HttpMethod))
        {
            await HandleDeleteAsync(request, response, client, ct).ConfigureAwait(false);
            return;
        }

        await HandlePostAsync(request, response, client, ct).ConfigureAwait(false);
    }

    // DELETE ends a session the client is done with. The id must be present and known,
    // and the standard mirrors the session-routing rule — a session is torn down by the
    // same client that opened it. An absent or unknown id is a 404; the spec lets the
    // client simply re-initialize.
    private async Task HandleDeleteAsync(
        HttpListenerRequest request, HttpListenerResponse response, McpClientProfile client, CancellationToken ct)
    {
        string? id = request.Headers[SessionHeader];
        if (string.IsNullOrEmpty(id) || !_sessions.TryGetValue(id, out McpSession? session))
        {
            await WriteStatusAsync(response, 404, ct).ConfigureAwait(false);
            return;
        }

        if (!ReferenceEquals(session.Client, client))
        {
            DeckleAnytypeMcpSource.Log.RequestRejected();
            DeckleAnytypeMcpSource.Log.RequestRejectedDetail("session-client-mismatch", 403);
            await WriteStatusAsync(response, 403, ct).ConfigureAwait(false);
            return;
        }

        _sessions.TryRemove(id, out _);
        await WriteStatusAsync(response, 204, ct).ConfigureAwait(false);
    }

    private async Task HandlePostAsync(
        HttpListenerRequest request, HttpListenerResponse response, McpClientProfile client, CancellationToken ct)
    {
        (string? body, bool tooLarge) = await ReadBodyAsync(request, ct).ConfigureAwait(false);
        if (tooLarge)
        {
            await WriteStatusAsync(response, 413, ct).ConfigureAwait(false);
            return;
        }

        JsonNode? parsed;
        try
        {
            parsed = body is null ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            // Parse failure is a JSON-RPC-level error, not a transport one: the request
            // arrived intact, so it earns a 200 carrying the standard parse-error object.
            await WriteJsonAsync(response, 200, McpServer.ProtocolError(null, -32700, "Parse error"), ct)
                .ConfigureAwait(false);
            return;
        }

        // A batch is a JSON array. This host answers one message per request, so a batch
        // is refused at the JSON-RPC layer (200 + invalid-request), never parsed apart.
        if (parsed is JsonArray)
        {
            await WriteJsonAsync(response, 200, McpServer.ProtocolError(null, -32600, "Batch requests are not supported"), ct)
                .ConfigureAwait(false);
            return;
        }

        if (parsed is not JsonObject message)
        {
            await WriteJsonAsync(response, 200, McpServer.ProtocolError(null, -32700, "Parse error"), ct)
                .ConfigureAwait(false);
            return;
        }

        McpSession session;
        // A non-string method node stays null here rather than throwing, so it slips past
        // the initialize gate into the session-routed path where McpServer answers it with
        // the proper JSON-RPC error — the transport never has to judge message shape.
        string? method = message["method"] is JsonValue methodValue
            && methodValue.TryGetValue<string>(out string? m) ? m : null;
        if (method == "initialize")
        {
            // A fresh initialize opens a session bound to this bearer's client. Reap the
            // stale before minting so the table stays bounded, then hand the new id back
            // in the response header the client will echo on every follow-up.
            EvictIdleSessions();
            session = new McpSession(client, _api);
            _sessions[session.Id] = session;
            DeckleAnytypeMcpSource.Log.SessionOpened();
            DeckleAnytypeMcpSource.Log.SessionOpenedDetail(client.Id, session.Id[..8]);
            response.AddHeader(SessionHeader, session.Id);
        }
        else
        {
            // Every non-initialize request must name its session. Absent is a protocol
            // slip (400); unknown means the server forgot it (404) and the spec has the
            // client re-initialize. A known session must be addressed by the same client
            // that opened it — a bearer swap under a borrowed id is refused (403).
            string? id = request.Headers[SessionHeader];
            if (string.IsNullOrEmpty(id))
            {
                DeckleAnytypeMcpSource.Log.RequestRejected();
                DeckleAnytypeMcpSource.Log.RequestRejectedDetail("session-missing", 400);
                await WriteStatusAsync(response, 400, ct).ConfigureAwait(false);
                return;
            }
            if (!_sessions.TryGetValue(id, out McpSession? existing))
            {
                DeckleAnytypeMcpSource.Log.RequestRejected();
                DeckleAnytypeMcpSource.Log.RequestRejectedDetail("session-unknown", 404);
                await WriteStatusAsync(response, 404, ct).ConfigureAwait(false);
                return;
            }
            if (!ReferenceEquals(existing.Client, client))
            {
                DeckleAnytypeMcpSource.Log.RequestRejected();
                DeckleAnytypeMcpSource.Log.RequestRejectedDetail("session-client-mismatch", 403);
                await WriteStatusAsync(response, 403, ct).ConfigureAwait(false);
                return;
            }
            session = existing;
        }

        JsonObject? result = await session.HandleAsync(message, ct).ConfigureAwait(false);
        if (result is null)
        {
            // A notification draws no reply: the message was accepted, there is nothing
            // to return.
            await WriteStatusAsync(response, 202, ct).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(response, 200, result, ct).ConfigureAwait(false);
    }

    // ── Session eviction ──────────────────────────────────────────────────

    // Drop every session untouched past the idle limit. Run at each session creation so
    // the table cleans itself without a standing timer; the snapshot enumeration is safe
    // against the concurrent dictionary.
    private void EvictIdleSessions()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - SessionIdleLimit;
        foreach (var pair in _sessions)
        {
            if (pair.Value.LastActivity < cutoff)
                _sessions.TryRemove(pair.Key, out _);
        }
    }

    // ── Request helpers ───────────────────────────────────────────────────

    // Extract the bearer credential from the Authorization header, or null when the
    // header is absent or not a Bearer scheme. Never logs the value.
    private static string? ReadBearer(HttpListenerRequest request)
    {
        string? header = request.Headers["Authorization"];
        if (header is null)
            return null;

        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        string token = header[scheme.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    // Read the body, capped: returns tooLarge once the stream passes MaxBodyBytes so the
    // caller can answer 413 without buffering a runaway. An empty body reads back as an
    // empty string, distinguished from the too-large signal by the flag.
    private static async Task<(string? Body, bool TooLarge)> ReadBodyAsync(HttpListenerRequest request, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await request.InputStream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return (null, true);
            buffer.Write(chunk, 0, read);
        }

        return (Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), false);
    }

    // A present Origin must resolve to loopback: http(s)://localhost or 127.0.0.1, with
    // or without a port. Anything else is a cross-site caller and is refused.
    private static bool IsLocalOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host == "127.0.0.1";
    }

    // ── Response writers ──────────────────────────────────────────────────

    private static async Task WriteStatusAsync(HttpListenerResponse response, int status, CancellationToken ct)
    {
        response.StatusCode = status;
        response.ContentLength64 = 0;
        try
        {
            await response.OutputStream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            response.Close();
        }
    }

    private static async Task WriteJsonAsync(
        HttpListenerResponse response, int status, JsonNode payload, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        try
        {
            await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }
        finally
        {
            response.Close();
        }
    }

    // ── Shutdown ──────────────────────────────────────────────────────────

    // Cancel the loop and close the listener, absorbing the exception the pending
    // GetContextAsync throws as the listener drops out from under it. The accept loop is
    // awaited under a bounded cap so shutdown cannot hang on a wedged task; HostStopped
    // is emitted exactly once, on the first disposal.
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cts.Cancel();

        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // Already torn down, or never started — nothing to close.
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The loop is wedged past the cap; let disposal proceed rather than block
                // the caller. The listener is already closed, so no new work can arrive.
            }
            catch (OperationCanceledException)
            {
                // Expected: the loop unwound on cancellation.
            }
        }

        // Drain the in-flight handlers under the same bounded posture. The
        // listener is closed so nothing new can arrive; what remains is tool
        // calls racing the AnytypeApiClient the caller disposes right after this
        // returns. A handler wedged past the cap (a hung backend request) is
        // abandoned to the process exit that follows QuitApp — bounded beats
        // hung.
        Task[] pending = _inFlight.Keys.ToArray();
        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
        }

        _cts.Dispose();
        if (_started)
            DeckleAnytypeMcpSource.Log.HostStopped();
    }
}

// HttpListenerRequest.HttpMethod is a bare string; these keep the method comparisons
// case-insensitive and readable at the call sites.
internal static class HttpMethods
{
    public static bool IsGet(string method) => string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);
    public static bool IsPost(string method) => string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
    public static bool IsDelete(string method) => string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase);
}
