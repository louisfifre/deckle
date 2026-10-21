using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// Thin transport over the local Anytype REST API. No domain knowledge: every
// method takes/returns a JsonNode and the gestures layer owns the payload
// shapes. Two robustness obligations:
//
//   • Serialize all calls through a SemaphoreSlim. The API tolerates ~1 rps
//     sustained with a burst of 60; one in-flight request at a time keeps us
//     well inside that envelope and makes ordering deterministic for the
//     read-modify-write gestures (markdown PATCH is a full replacement).
//   • Retry once on 429, and on 5xx only when the exact request is safe to
//     replay, honoring Retry-After when present. A create may have committed
//     before a 5xx and must surface that ambiguity instead of duplicating.
//
// Wire facts: headers Anytype-Version + Authorization: Bearer on every call;
// a single object is wrapped in {object:…}; search/list roots carry
// {data, pagination}. The Bearer token is held only in the Authorization
// header and never logged.
public sealed partial class AnytypeApiClient : IDisposable
{
    private enum RequestReplaySafety { Unsafe, Safe }

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _spacePath;
    private readonly SpaceWriteLock _writeLock;
    private readonly IBackendEndpointTrust? _endpointTrust;

    // One transport incident across calls: the first unanswered request logs
    // the Error, the next ones stay Verbose, the first answer closes it. Only
    // touched inside _gate, so no further synchronization.
    private int _transportFailures;
    private long _transportFailingSince;

    // DefaultBackoff applies when the response omits Retry-After; small because
    // the gate already paces traffic.
    private const int MaxRetries = 1;
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(1);

    public string SpaceId { get; }

    public AnytypeApiClient(AnytypeCredentials credentials)
        : this(credentials, endpointTrust: null)
    {
    }

    // The resident gateway re-proves ownership of the supervised headless
    // listener (31012) before putting its bearer on the wire. The legacy Desktop
    // pairing and explicit test endpoints stay outside provider supervision.
    public static AnytypeApiClient CreateForResidentGateway(AnytypeCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var endpoint = new Uri(credentials.ApiUrl);
        IBackendEndpointTrust? trust = endpoint.IsLoopback && endpoint.Port == 31012
            ? BackendEndpointTrust.CreateDefault(endpoint)
            : null;
        return new(credentials, trust);
    }

    internal AnytypeApiClient(
        AnytypeCredentials credentials,
        IBackendEndpointTrust? endpointTrust)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        SpaceId = credentials.SpaceId;
        _spacePath = $"/v1/spaces/{credentials.SpaceId}";

        _http = new HttpClient { BaseAddress = new Uri(credentials.ApiUrl) };
        _http.DefaultRequestHeaders.Add("Anytype-Version", credentials.ApiVersion);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", credentials.ApiKey);
        _endpointTrust = endpointTrust;

        // The cross-process write lock lives beside the credentials, in the
        // anytype module directory (same id as AnytypeCredentials.ModuleId).
        _writeLock = new SpaceWriteLock(AppPaths.GetModuleDirectory("anytype"));
    }

    // Exclusive write access to the space across every host process. A mutating
    // gesture wraps its whole read-modify-write in this scope so a concurrent
    // session cannot land a clobbering PATCH between its GET and its own PATCH.
    public Task<IDisposable> AcquireWriteScopeAsync(
        string operation, string target, CancellationToken ct = default)
        => _writeLock.AcquireAsync(operation, target, ct);

    // ── Public API ──────────────────────────────────────────────────────

    public Task<JsonObject> GetObjectAsync(string id, CancellationToken ct = default) =>
        GetObjectAsync(SpaceId, id, ct);

    public async Task<JsonObject> GetObjectAsync(
        string spaceId, string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        JsonObject root = await SendAsync(
                HttpMethod.Get, $"{SpacePath(spaceId)}/objects/{id}", null, ct,
                replaySafety: RequestReplaySafety.Safe)
            .ConfigureAwait(false);
        return Inner(root, "object");
    }

    // Sort is the API default, last-modified desc. offset/limit ride in the
    // query string: the request body has no paging field, so a body `limit` is
    // silently dropped and the server pages by its default of 100. An
    // exhaustive read walks pagination.has_more.
    public Task<JsonObject> SearchAsync(
        string query,
        IReadOnlyList<string>? typeKeys = null,
        int offset = 0,
        int limit = 20,
        CancellationToken ct = default) =>
        SearchAsync(SpaceId, query, typeKeys, offset, limit, ct);

    public async Task<JsonObject> SearchAsync(
        string spaceId,
        string query,
        IReadOnlyList<string>? typeKeys = null,
        int offset = 0,
        int limit = 20,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > SearchPageMax) throw new ArgumentOutOfRangeException(nameof(limit));

        var body = new JsonObject { ["query"] = query };
        if (typeKeys is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (string key in typeKeys) arr.Add(key);
            body["types"] = arr;
        }

        string path = $"{SpacePath(spaceId)}/search?offset={offset}&limit={limit}";
        return await SendAsync(
                HttpMethod.Post, path, body, ct,
                replaySafety: RequestReplaySafety.Safe)
            .ConfigureAwait(false);
    }

    // The largest page the search endpoint serves — its documented maximum.
    public const int SearchPageMax = 1000;

    // The exhaustive path for registry and uniqueness checks: free-text search
    // is deliberately not treated as an inventory index.
    public async Task<JsonObject> ListObjectsAsync(
        string spaceId,
        int offset = 0,
        int limit = 1000,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));

        string path = $"{SpacePath(spaceId)}/objects?offset={offset}&limit={limit}";
        return await SendAsync(
            HttpMethod.Get, path, null, ct, replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
    }

    public Task<JsonObject> CreateObjectAsync(JsonObject payload, CancellationToken ct = default) =>
        CreateObjectAsync(SpaceId, payload, ct);

    public async Task<JsonObject> CreateObjectAsync(
        string spaceId, JsonObject payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentNullException.ThrowIfNull(payload);
        JsonObject root = await SendAsync(
                HttpMethod.Post, $"{SpacePath(spaceId)}/objects", payload, ct)
            .ConfigureAwait(false);
        return Inner(root, "object");
    }

    // The markdown field is a FULL REPLACEMENT of the body, never an append
    // (API-level constraint).
    public Task<JsonObject> UpdateObjectAsync(
        string id, JsonObject payload, CancellationToken ct = default) =>
        UpdateObjectAsync(SpaceId, id, payload, ct);

    public async Task<JsonObject> UpdateObjectAsync(
        string spaceId, string id, JsonObject payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(payload);
        JsonObject root = await SendAsync(
                HttpMethod.Patch, $"{SpacePath(spaceId)}/objects/{id}", payload, ct,
                replaySafety: RequestReplaySafety.Safe)
            .ConfigureAwait(false);
        return Inner(root, "object");
    }

    // DELETE moves the object to Anytype's restorable bin (the native trash),
    // NOT a hard delete. The answer may echo the archived object or be empty.
    public Task<JsonObject> DeleteObjectAsync(string id, CancellationToken ct = default) =>
        DeleteObjectAsync(SpaceId, id, ct);

    public async Task<JsonObject> DeleteObjectAsync(
        string spaceId, string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        JsonObject root = await SendAsync(
                HttpMethod.Delete, $"{SpacePath(spaceId)}/objects/{id}", null, ct)
            .ConfigureAwait(false);
        return root["object"] as JsonObject ?? root;
    }

    public async Task<JsonObject> ListPropertiesAsync(
        int offset = 0, int limit = 100, CancellationToken ct = default)
    {
        string path = $"{_spacePath}/properties?offset={offset}&limit={limit}";
        return await SendAsync(
            HttpMethod.Get, path, null, ct, replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
    }

    // The path takes the property ID; the key form 404s. Resolve the key to an
    // id through ListPropertiesAsync first.
    public async Task<JsonObject> ListPropertyTagsAsync(
        string propertyId, int offset = 0, int limit = 100, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        string path = $"{_spacePath}/properties/{propertyId}/tags?offset={offset}&limit={limit}";
        return await SendAsync(
            HttpMethod.Get, path, null, ct, replaySafety: RequestReplaySafety.Safe).ConfigureAwait(false);
    }

    // A collection IS a list in the API. The success body is a bare JSON
    // string, not an object, so it is not parsed.
    public Task AddToCollectionAsync(
        string collectionId,
        IReadOnlyList<string> objectIds,
        CancellationToken ct = default) =>
        AddToCollectionAsync(SpaceId, collectionId, objectIds, ct);

    public async Task AddToCollectionAsync(
        string spaceId,
        string collectionId,
        IReadOnlyList<string> objectIds,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        ArgumentNullException.ThrowIfNull(objectIds);
        if (objectIds.Count == 0)
            throw new ArgumentException("At least one object id is required.", nameof(objectIds));

        var arr = new JsonArray();
        foreach (string objectId in objectIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
            arr.Add(objectId);
        }
        var body = new JsonObject { ["objects"] = arr };

        await SendAsync(
            HttpMethod.Post,
            $"{SpacePath(spaceId)}/lists/{collectionId}/objects",
            body,
            ct,
            parseBody: false).ConfigureAwait(false);
    }

    // Removes list membership only; the object itself is untouched.
    public async Task RemoveFromCollectionAsync(
        string spaceId,
        string collectionId,
        string objectId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);

        await SendAsync(
            HttpMethod.Delete,
            $"{SpacePath(spaceId)}/lists/{collectionId}/objects/{objectId}",
            null,
            ct,
            parseBody: false).ConfigureAwait(false);
    }

    // ── Transport core ──────────────────────────────────────────────────

    private Task<JsonObject> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        CancellationToken ct,
        bool parseBody = true,
        RequestReplaySafety replaySafety = RequestReplaySafety.Unsafe) =>
        SendContentAsync(
            method,
            path,
            body is null
                ? null
                : () => new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            ct,
            parseBody,
            replaySafety);

    // The body arrives as a FACTORY, not a built HttpContent: an HttpContent is
    // single-use, so the retry attempt needs a fresh one. That is also what lets
    // multipart uploads share this transport instead of bypassing the gate.
    private async Task<JsonObject> SendContentAsync(
        HttpMethod method,
        string path,
        Func<HttpContent>? content,
        CancellationToken ct,
        bool parseBody = true,
        RequestReplaySafety replaySafety = RequestReplaySafety.Unsafe)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                if (_endpointTrust is not null && !_endpointTrust.IsTrusted())
                {
                    DeckleAnytypeSource.Log.ApiRequestFailed(
                        method.Method, 0, "untrusted_endpoint", "", "");
                    DeckleAnytypeSource.Log.ApiRequestFailedDetail(
                        method.Method, path, 0, "endpoint owner is not a trusted provider");
                    throw new InvalidOperationException(
                        "The Anytype endpoint is not owned by a trusted Deckle provider; the request was not sent.");
                }

                bool traceRequest = DeckleAnytypeSource.Log.IsEnabled(
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Network);
                long startTicks = traceRequest ? Stopwatch.GetTimestamp() : 0;
                if (traceRequest)
                    DeckleAnytypeSource.Log.ApiRequestStarted(method.Method, path);

                using var request = new HttpRequestMessage(method, path);
                if (content is not null) request.Content = content();

                HttpResponseMessage sent;
                try
                {
                    sent = await _http.SendAsync(request, ct).ConfigureAwait(false);
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
                            method.Method, 0, reason, ex.GetType().Name, ex.Message);
                    }
                    DeckleAnytypeSource.Log.ApiRequestFailedDetail(
                        method.Method, path, 0, $"{ex.GetType().Name}: {ex.Message}");
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

                double elapsedMs = traceRequest
                    ? Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds
                    : 0;
                int status = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    if (traceRequest)
                        DeckleAnytypeSource.Log.ApiRequestCompleted(method.Method, path, status, elapsedMs);
                    if (!parseBody) return new JsonObject();
                    string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return ParseRoot(json);
                }

                if (attempt < MaxRetries && IsRetryable(response.StatusCode, replaySafety))
                {
                    TimeSpan backoff = RetryAfter(response) ?? DefaultBackoff;
                    if (traceRequest)
                    {
                        DeckleAnytypeSource.Log.ApiRequestRetriedDetail(
                            method.Method, path, status, backoff.TotalMilliseconds);
                    }
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                    continue;
                }

                // The server message is API error text, never key material, so
                // it may be logged — in the Verbose detail only: it is free text
                // the milestone cannot bound, and the status already says why.
                string errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                DeckleAnytypeSource.Log.ApiRequestFailed(method.Method, status, "http_status", "", "");
                DeckleAnytypeSource.Log.ApiRequestFailedDetail(method.Method, path, status, Excerpt(errorBody));
                throw new HttpRequestException(
                    $"Anytype API {method.Method} {path} failed with {status}: {Excerpt(errorBody)}",
                    null, response.StatusCode);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // A 429 means the request was refused before execution and is replayable.
    // A 5xx can arrive after the provider committed, so only callers whose
    // exact wire effect is idempotent may opt into replaying it.
    private static bool IsRetryable(HttpStatusCode code, RequestReplaySafety replaySafety) =>
        code == HttpStatusCode.TooManyRequests
        || (replaySafety == RequestReplaySafety.Safe && (int)code >= 500);

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta) return delta;
        if (header.Date is { } date)
        {
            TimeSpan delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }
        return null;
    }

    private static JsonObject ParseRoot(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        return JsonNode.Parse(json) as JsonObject
            ?? throw new HttpRequestException("Anytype API returned a non-object JSON root.");
    }

    private static JsonObject Inner(JsonObject root, string key)
    {
        return root[key] as JsonObject
            ?? throw new HttpRequestException($"Anytype API response is missing the \"{key}\" node.");
    }

    // Bound the logged error text so a stray HTML error page from a reverse
    // proxy doesn't flood the listener.
    private static string Excerpt(string text)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        return text.Length > 200 ? text[..200] + "…" : text;
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
