using System.Diagnostics;
using System.Net.Http;

namespace Deckle.Anytype;

// ── BackendHealthProbe ───────────────────────────────────────────────────────
//
// Answers one question: is the Anytype backend's REST listener up? The health
// signal is `GET /docs/openapi.json` returning 200 — unauthenticated, and chosen
// over a port ping or a PID check because the listener only binds after the
// account login propagates the listen address, so a 200 here proves the REST
// surface is actually serving (verified 2026-06-18, see JOURNAL). The PID is
// never the signal: the task process can be alive while REST is not yet bound.
//
// Separate from AnytypeApiClient on purpose — that client is bound to a space and
// a bearer token and speaks /v1; this probe is unauthenticated and hits the root
// docs endpoint, a different responsibility with no credentials.
public sealed class BackendHealthProbe : IDisposable
{
    // The headless backend's fixed loopback address (frozen 2026-06-18). Desktop
    // Anytype uses 31007-31009; the headless REST default is 31012.
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

    // True when the backend answers 200 on the health endpoint. Any failure —
    // connection refused, timeout, non-200 — means "not (yet) up" and returns
    // false; the probe never throws.
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        long startTicks = Stopwatch.GetTimestamp();
        try
        {
            using HttpResponseMessage response =
                await _http.GetAsync(HealthPath, ct).ConfigureAwait(false);

            double elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;
            bool healthy = response.IsSuccessStatusCode;
            DeckleAnytypeSource.Log.BackendHealthProbed(healthy, (int)response.StatusCode, elapsedMs);
            return healthy;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Connection refused (backend down) or our own probe timeout (backend
            // not yet bound). Status 0 marks "no HTTP response".
            double elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;
            DeckleAnytypeSource.Log.BackendHealthProbed(false, 0, elapsedMs);
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
