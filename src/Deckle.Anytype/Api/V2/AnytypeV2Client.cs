using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// Transport over the local Anytype REST API, version 2. It sits beside
// AnytypeApiClient (v1), which stays as it is: each gesture picks its version.
// No domain knowledge here either — callers describe a request, this class
// sends it and returns the answer or throws an AnytypeV2Exception.
//
// What differs from v1:
//
//   • Base path /v2/spaces/{id}. The Anytype-Version header is not sent: /v2
//     ignores it (measured), so requiring it would only invent a coupling.
//   • Writes can carry If-Match (a version read earlier; a stale one is a 409
//     etag_mismatch) and Idempotency-Key (a replay answers with the stored
//     result instead of acting twice). A write that carries a key is therefore
//     safe to resend after a 5xx, which v1 could never allow for a create.
//   • The 1 rps limiter is shared by every client of the backend, the resident
//     Deckle included. Only writes show its headers; dry runs spend the budget
//     too. A 429 is retried once after Retry-After (or Ratelimit-Reset).
//
// Same obligations as v1: one request in flight at a time, so read-modify-write
// gestures stay ordered and the burst stays small, and the bearer lives only in
// the Authorization header, never in a log. "At a time" holds across versions:
// the AnytypeRequestGate is the one v1 and v2 share when AnytypeApiClient built
// this client. Alone, it makes and owns its own gate.
public sealed class AnytypeV2Client : IDisposable
{
    private readonly HttpClient _http;
    private readonly AnytypeRequestGate _gate;
    // Only the creator of a gate disposes it: a borrowed one outlives this client.
    private readonly bool _ownsGate;
    private readonly string _spacePath;
    private readonly IBackendEndpointTrust? _endpointTrust;

    // One transport incident across calls: the first unanswered request logs
    // the Error, the next ones stay Verbose, the first answer closes it. Only
    // touched inside _gate, so no further synchronization.
    private int _transportFailures;
    private long _transportFailingSince;

    private const int MaxRetries = 1;
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(1);

    public string SpaceId { get; }

    public AnytypeV2Client(AnytypeCredentials credentials)
        : this(credentials, endpointTrust: null, handler: null)
    {
    }

    // Same rule as the v1 resident gateway: re-prove ownership of the supervised
    // headless listener before putting the bearer on the wire.
    public static AnytypeV2Client CreateForResidentGateway(AnytypeCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var endpoint = new Uri(credentials.ApiUrl);
        IBackendEndpointTrust? trust = endpoint.IsLoopback && endpoint.Port == 31012
            ? BackendEndpointTrust.CreateDefault(endpoint)
            : null;
        return new(credentials, trust, handler: null);
    }

    // The handler is the test seam: a fake one stands in for the backend. The
    // gate is borrowed from AnytypeApiClient when it builds this client; left
    // null, the client makes its own and disposes it.
    internal AnytypeV2Client(
        AnytypeCredentials credentials,
        IBackendEndpointTrust? endpointTrust,
        HttpMessageHandler? handler,
        AnytypeRequestGate? gate = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        _gate = gate ?? new AnytypeRequestGate();
        _ownsGate = gate is null;

        SpaceId = credentials.SpaceId;
        _spacePath = $"/v2/spaces/{credentials.SpaceId}";

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(credentials.ApiUrl);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", credentials.ApiKey);
        _endpointTrust = endpointTrust;
    }

    internal async Task<AnytypeV2Response> SendAsync(
        AnytypeV2Request request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        string path = $"{_spacePath}/{request.Path}{QueryString(request.Query)}";
        string method = request.Method.Method;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                if (_endpointTrust is not null && !_endpointTrust.IsTrusted())
                {
                    DeckleAnytypeSource.Log.ApiRequestFailed(method, 0, "untrusted_endpoint", "", "");
                    DeckleAnytypeSource.Log.ApiRequestFailedDetail(
                        method, path, 0, "endpoint owner is not a trusted provider");
                    throw new InvalidOperationException(
                        "The Anytype endpoint is not owned by a trusted Deckle provider; the request was not sent.");
                }

                bool traceRequest = DeckleAnytypeSource.Log.IsEnabled(
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Network);
                long startTicks = traceRequest ? Stopwatch.GetTimestamp() : 0;
                if (traceRequest)
                    DeckleAnytypeSource.Log.ApiRequestStarted(method, path);

                // An HttpRequestMessage and its content are single-use, so each
                // attempt builds its own.
                using var message = new HttpRequestMessage(request.Method, path);
                if (request.Body is not null)
                {
                    message.Content = new StringContent(
                        request.Body.ToJsonString(), Encoding.UTF8, "application/json");
                }
                if (request.IfMatch is not null)
                    message.Headers.TryAddWithoutValidation("If-Match", request.IfMatch);
                if (request.IdempotencyKey is not null)
                    message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);

                HttpResponseMessage sent;
                try
                {
                    sent = await _http.SendAsync(message, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    // A TaskCanceledException the caller did not ask for is
                    // HttpClient's own timeout.
                    string reason = ex is TaskCanceledException ? "timeout" : "transport";
                    if (_transportFailures++ == 0)
                    {
                        _transportFailingSince = Stopwatch.GetTimestamp();
                        DeckleAnytypeSource.Log.ApiRequestFailed(
                            method, 0, reason, ex.GetType().Name, ex.Message);
                    }
                    DeckleAnytypeSource.Log.ApiRequestFailedDetail(
                        method, path, 0, $"{ex.GetType().Name}: {ex.Message}");
                    throw;
                }
                using HttpResponseMessage response = sent;

                if (_transportFailures > 0)
                {
                    DeckleAnytypeSource.Log.ApiTransportRecovered(
                        _transportFailures,
                        Stopwatch.GetElapsedTime(_transportFailingSince).TotalSeconds);
                    _transportFailures = 0;
                }

                int status = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    if (traceRequest)
                    {
                        DeckleAnytypeSource.Log.ApiRequestCompleted(
                            method, path, status,
                            Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds);
                    }
                    string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return new AnytypeV2Response(
                        status,
                        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json),
                        response.Headers.ETag?.Tag,
                        HeaderValue(response, "Idempotency-Replayed") == "true",
                        int.TryParse(
                            HeaderValue(response, "Ratelimit-Remaining"),
                            NumberStyles.None, CultureInfo.InvariantCulture, out int remaining)
                            ? remaining : null);
                }

                if (attempt < MaxRetries && IsRetryable(response.StatusCode, request.IsReplaySafe))
                {
                    TimeSpan backoff = RetryAfter(response) ?? DefaultBackoff;
                    if (traceRequest)
                    {
                        DeckleAnytypeSource.Log.ApiRequestRetriedDetail(
                            method, path, status, backoff.TotalMilliseconds);
                    }
                    // Waited inside the gate on purpose: a 429 says the shared
                    // budget is spent, so no other request may go out meanwhile.
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                    continue;
                }

                // The envelope is API error text, never key material. It rides
                // in the exception; the log keeps the Verbose detail the v1
                // client keeps, since the status already says why.
                string errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                AnytypeV2Exception failure = AnytypeV2Exception.From(
                    method, path, response.StatusCode, errorBody);
                DeckleAnytypeSource.Log.ApiRequestFailed(method, status, "http_status", "", "");
                DeckleAnytypeSource.Log.ApiRequestFailedDetail(method, path, status, failure.Message);
                throw failure;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // A 429 means the request was refused before execution and is replayable.
    // A 5xx can arrive after the backend committed, so only a request the
    // server can recognize as a repeat may be resent.
    private static bool IsRetryable(HttpStatusCode code, bool replaySafe) =>
        code == HttpStatusCode.TooManyRequests
        || (replaySafe && (int)code >= 500);

    // The shared limiter names its wait twice: the standard Retry-After, and the
    // Ratelimit-Reset header (seconds) that only writes carry.
    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date)
        {
            TimeSpan delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }
        return double.TryParse(
                HeaderValue(response, "Ratelimit-Reset"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static string? HeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static string QueryString(IReadOnlyList<KeyValuePair<string, string>>? query)
    {
        if (query is not { Count: > 0 }) return "";
        return "?" + string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    public void Dispose()
    {
        _http.Dispose();
        if (_ownsGate) _gate.Dispose();
    }
}
