using System.Net;
using System.Threading.RateLimiting;
using Deckle.Anytype;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Deckle.Anytype.Mcp;

// The one resident Streamable HTTP MCP host from ADR-0002. Kestrel owns HTTP
// framing, request draining and the loopback listener; the official MCP SDK owns
// protocol negotiation and JSON-RPC. Deckle keeps the security boundary around
// both: a present Origin must be local and every request must authenticate with
// the bearer that selects its tool surface.
public sealed class McpHttpHost : IAsyncDisposable
{
    // Loopback, outside every neighbour's range: 3100x-3101x is Anytype, 11434 is
    // Ollama, and the Windows dynamic range opens at 49152.
    public const int DefaultPort = 33255;

    public const string EndpointPath = "/mcp";

    // A legitimate tool call is kilobytes. Kestrel rejects anything past 4 MiB
    // before the MCP transport buffers or parses it.
    public const long MaxRequestBodyBytes = 4L * 1024 * 1024;

    private static readonly object ClientProfileKey = new();
    private const string RateLimitPolicy = "authenticated-mcp-client";

    private readonly AnytypeApiClient _api;
    private readonly McpClientTokens _tokens;
    private readonly int _port;
    private readonly McpRequestRateLimit _requestRateLimit;
    private readonly object _stateGate = new();

    private WebApplication? _application;
    private Task? _disposeTask;
    private bool _started;

    public McpHttpHost(AnytypeApiClient api, McpClientTokens tokens, int port = DefaultPort)
        : this(api, tokens, port, McpRequestRateLimit.Default)
    {
    }

    internal McpHttpHost(
        AnytypeApiClient api,
        McpClientTokens tokens,
        int port,
        McpRequestRateLimit requestRateLimit)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(requestRateLimit);
        _api = api;
        _tokens = tokens;
        _port = port;
        _requestRateLimit = requestRateLimit;
    }

    public string BaseUrl => $"http://127.0.0.1:{_port}{EndpointPath}";

    // Keep the app composition contract synchronous for now. Kestrel is fully
    // started before true is returned, so external clients never observe a URL
    // that Deckle has announced but not yet bound.
    public bool Start()
    {
        lock (_stateGate)
        {
            if (_started)
                return true;
            if (_disposeTask is not null)
                return false;

            WebApplication application = BuildApplication();
            try
            {
                application.StartAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                application.DisposeAsync().AsTask().GetAwaiter().GetResult();
                DeckleAnytypeMcpSource.Log.RequestRejected();
                DeckleAnytypeMcpSource.Log.RequestRejectedDetail($"bind-failed: {ex.Message}", 0);
                return false;
            }

            _application = application;
            _started = true;
        }

        DeckleAnytypeMcpSource.Log.HostStarted();
        DeckleAnytypeMcpSource.Log.HostStartedDetail(BaseUrl);
        return true;
    }

    private WebApplication BuildApplication()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
            options.Listen(IPAddress.Loopback, _port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1AndHttp2;
            });
        });

        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                // Explicit even though SDK v2 defaults to stateless: this is the
                // architecture contract, not an incidental package default.
                options.Stateless = true;
                options.ConfigureSessionOptions = (context, serverOptions, _) =>
                {
                    if (!context.Items.TryGetValue(ClientProfileKey, out object? value)
                        || value is not McpClientProfile client)
                    {
                        throw new InvalidOperationException("The MCP request has no authenticated client profile.");
                    }

                    McpSurfaceProtocolAdapter.Configure(serverOptions, client.Surface.Open(_api));
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                Reject("rate-limit", StatusCodes.Status429TooManyRequests);
                return ValueTask.CompletedTask;
            };
            options.AddPolicy<string>(RateLimitPolicy, context =>
            {
                if (!context.Items.TryGetValue(ClientProfileKey, out object? value)
                    || value is not McpClientProfile client)
                {
                    return RateLimitPartition.GetNoLimiter("unauthenticated");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    client.Id,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = _requestRateLimit.PermitLimit,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        Window = _requestRateLimit.Window,
                    });
            });
        });

        WebApplication application = builder.Build();
        application.Use(ProtectEndpointAsync);
        application.UseRateLimiter();
        application.MapMcp(EndpointPath).RequireRateLimiting(RateLimitPolicy);
        return application;
    }

    private async Task ProtectEndpointAsync(HttpContext context, RequestDelegate next)
    {
        if (!string.Equals(context.Request.Path.Value, EndpointPath, StringComparison.Ordinal))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Stateless Streamable HTTP has one request channel. Standalone GET and
        // session DELETE were removed by the 2026-07-28 protocol revision.
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.Headers.Allow = "POST";
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        if (context.Request.ContentLength is > MaxRequestBodyBytes)
        {
            Reject("body-too-large", StatusCodes.Status413PayloadTooLarge);
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        string origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsLocalOrigin(origin))
        {
            Reject("foreign-origin", StatusCodes.Status403Forbidden);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        string? bearer = ReadBearer(context.Request.Headers.Authorization.ToString());
        McpClientProfile? client = _tokens.Authenticate(bearer);
        if (client is null)
        {
            Reject(bearer is null ? "missing-bearer" : "unknown-bearer",
                StatusCodes.Status401Unauthorized);
            context.Response.Headers.WWWAuthenticate = "Bearer";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items[ClientProfileKey] = client;
        await next(context).ConfigureAwait(false);
    }

    private static string? ReadBearer(string authorization)
    {
        const string scheme = "Bearer ";
        if (!authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        string token = authorization[scheme.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    private static bool IsLocalOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host == "127.0.0.1";
    }

    private static void Reject(string reason, int statusCode)
    {
        DeckleAnytypeMcpSource.Log.RequestRejected();
        DeckleAnytypeMcpSource.Log.RequestRejectedDetail(reason, statusCode);
    }

    public ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    // Stops accepting new requests and positively reports whether every active
    // request drained within the caller's shutdown budget. A false result leaves
    // the application and its AnytypeApiClient alive: process exit may finish the
    // teardown, but Deckle must not dispose the API under a handler still using it.
    public async Task<bool> StopAsync(CancellationToken ct)
    {
        WebApplication? application;
        lock (_stateGate)
            application = _application;

        if (application is null)
            return true;

        try
        {
            await application.StopAsync(ct).ConfigureAwait(false);
            // Kestrel may consume shutdown cancellation and complete normally;
            // an expired caller budget still cannot certify a complete drain.
            return !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task DisposeCoreAsync()
    {
        WebApplication? application = _application;
        if (application is null)
            return;

        try
        {
            // StopAsync first closes the listener, then waits for active requests.
            // The caller owns any outer shutdown budget; the host itself never
            // abandons a tool call and disposes its dependencies underneath it.
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await application.DisposeAsync().ConfigureAwait(false);
            DeckleAnytypeMcpSource.Log.HostStopped();
        }
    }
}

internal static class HttpMethods
{
    public static bool IsPost(string method) =>
        string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
}
