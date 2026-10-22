using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;

namespace Deckle.Anytype;

// Answers one question: is the Anytype backend's REST listener up? The health
// signal is `GET /docs/openapi.json` returning 200 — unauthenticated, and chosen
// over a port ping or a PID check because the listener only binds after the
// account login propagates the listen address, so a 200 here proves the REST
// surface is actually serving. Since anytype-heart 0.51 this root route serves
// the v2 OpenAPI document and v1 lives under `/v1/docs/openapi.json`; the probe
// only needs the unauthenticated 200, so it stays valid on either. On 0.5.0 that
// document lists 41 `/v2` paths and no `/v1` one, yet the `/v1/*` routes still
// answer (401 without a key, so alive): the root document does not tell which
// API versions are served. The PID is
// never the signal: the task process can be alive while REST is not yet bound.
//
// Separate from AnytypeApiClient on purpose — that client is bound to a space and
// a bearer token and speaks /v1; this probe is unauthenticated and hits the root
// docs endpoint, a different responsibility with no credentials.
internal interface IBackendHealthProbe : IDisposable
{
    Task<BackendHealth> ProbeAsync(CancellationToken ct = default);
}

// Reason is one snake_case word (ok, http_status, timeout, refused,
// unreachable) so the supervisor's unresponsive warning can carry the cause.
internal readonly record struct BackendHealth(bool Healthy, string Reason);

public sealed class BackendHealthProbe : IBackendHealthProbe
{
    // The headless backend's fixed loopback address. Desktop Anytype uses
    // 31007-31009; the headless REST default is 31012.
    public const string DefaultBaseUrl = "http://127.0.0.1:31012";

    private const string HealthPath = "/docs/openapi.json";

    // A probe of a down backend returns "connection refused" promptly; a probe of
    // a still-starting one can hang, so a short per-probe timeout bounds it.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;

    public BackendHealthProbe(string? baseUrl = null)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl ?? DefaultBaseUrl),
            Timeout     = ProbeTimeout,
        };
    }

    // Any failure — connection refused, timeout, non-200 — means "not (yet) up";
    // only the caller's cancellation propagates.
    async Task<BackendHealth> IBackendHealthProbe.ProbeAsync(CancellationToken ct)
    {
        long startTicks = Stopwatch.GetTimestamp();
        try
        {
            using HttpResponseMessage response =
                await _http.GetAsync(HealthPath, ct).ConfigureAwait(false);

            double elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;
            bool healthy = response.IsSuccessStatusCode;
            string reason = healthy ? "ok" : "http_status";
            DeckleAnytypeSource.Log.BackendHealthProbed(healthy, (int)response.StatusCode, reason, elapsedMs);
            return new BackendHealth(healthy, reason);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Status 0 marks "no HTTP response". A refused connection is the
            // normal answer of a stopped backend; anything else is worth telling apart.
            double elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;
            string reason = ex switch
            {
                TaskCanceledException => "timeout",
                HttpRequestException { InnerException: SocketException { SocketErrorCode: SocketError.ConnectionRefused } } => "refused",
                _ => "unreachable",
            };
            DeckleAnytypeSource.Log.BackendHealthProbed(false, 0, reason, elapsedMs);
            return new BackendHealth(false, reason);
        }
    }

    public void Dispose() => _http.Dispose();
}
