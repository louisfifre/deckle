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
// shapes. Two robustness obligations, both founding decisions (mcp/JOURNAL.md):
//
//   • Serialize all calls through a SemaphoreSlim. The API tolerates ~1 rps
//     sustained with a burst of 60; one in-flight request at a time keeps us
//     well inside that envelope and makes ordering deterministic for the
//     read-modify-write gestures (markdown PATCH is a full replacement).
//   • Retry once on 429/5xx, honoring Retry-After when present. A transient
//     5xx or a rate-limit nudge should not surface as a gesture failure.
//
// Wire facts (verified against the live API 2026-06-12 and the vendor JS
// reference): base http://localhost:31009; headers Anytype-Version +
// Authorization: Bearer on every call; single object is wrapped in {object:…};
// search/list roots carry {data, pagination}; list-add answers a bare JSON
// string ("Objects added successfully"), not an object. The Bearer token is
// held only in the Authorization header and never logged.
public sealed partial class AnytypeApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _spacePath;
    private readonly SpaceWriteLock _writeLock;

    // One retry on a transient. Backoff falls back to this when the response
    // omits Retry-After; small because the gate already paces traffic.
    private const int MaxRetries = 1;
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(1);

    public string SpaceId { get; }

    public AnytypeApiClient(AnytypeCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        SpaceId = credentials.SpaceId;
        _spacePath = $"/v1/spaces/{credentials.SpaceId}";

        _http = new HttpClient { BaseAddress = new Uri(credentials.ApiUrl) };
        _http.DefaultRequestHeaders.Add("Anytype-Version", credentials.ApiVersion);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", credentials.ApiKey);

        // The cross-process write lock lives beside the credentials, in the
        // anytype module directory (same id as AnytypeCredentials.ModuleId).
        _writeLock = new SpaceWriteLock(AppPaths.GetModuleDirectory("anytype"));
    }

    // Exclusive write access to the space across every host process. A mutating
    // gesture wraps its whole read-modify-write in this scope so a concurrent
    // session cannot land a clobbering PATCH between its GET and its own PATCH.
    // operation/target name the pending write for the contention log.
    public Task<IDisposable> AcquireWriteScopeAsync(
        string operation, string target, CancellationToken ct = default)
        => _writeLock.AcquireAsync(operation, target, ct);

    // ── Public API ──────────────────────────────────────────────────────

    // GET object → returns the inner "object" node (the API wraps it as
    // {object:{…}}).
    public Task<JsonObject> GetObjectAsync(string id, CancellationToken ct = default) =>
        GetObjectAsync(SpaceId, id, ct);

    public async Task<JsonObject> GetObjectAsync(
        string spaceId, string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        JsonObject root = await SendAsync(
                HttpMethod.Get, $"{SpacePath(spaceId)}/objects/{id}", null, ct)
            .ConfigureAwait(false);
        return Inner(root, "object");
    }

    // POST search → returns the root node ({data, pagination}). typeKeys null
    // → no type filter. sort by last-modified desc is the API default; the
    // gestures pass an explicit sort when they need another order.
    public Task<JsonObject> SearchAsync(
        string query,
        IReadOnlyList<string>? typeKeys = null,
        int limit = 20,
        CancellationToken ct = default) =>
        SearchAsync(SpaceId, query, typeKeys, limit, ct);

    public async Task<JsonObject> SearchAsync(
        string spaceId,
        string query,
        IReadOnlyList<string>? typeKeys = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);

        var body = new JsonObject
        {
            ["query"] = query,
            ["limit"] = limit
        };
        if (typeKeys is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (string key in typeKeys) arr.Add(key);
            body["types"] = arr;
        }

        return await SendAsync(
                HttpMethod.Post, $"{SpacePath(spaceId)}/search", body, ct)
            .ConfigureAwait(false);
    }

    // GET all objects for one space page. Home uses this exhaustive path for
    // room-registry and code-uniqueness checks: free-text search is deliberately
    // not treated as an inventory index.
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
        return await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
    }

    // POST object → returns the inner "object" node. payload carries type_key
    // plus name/body/properties per the vendor wire shape (createObject).
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

    // PATCH object → returns the inner "object" node. The markdown field is a
    // FULL REPLACEMENT of the body, never an append (API-level constraint).
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
                HttpMethod.Patch, $"{SpacePath(spaceId)}/objects/{id}", payload, ct)
            .ConfigureAwait(false);
        return Inner(root, "object");
    }

    // DELETE object → moves it to Anytype's restorable bin (the native trash),
    // NOT a hard delete (verified 2026-06-12, see JOURNAL). Returns the inner
    // "object" node when the API echoes the archived object, and tolerates an
    // empty body by returning the bare root.
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

    // GET the space's properties (one page) → returns the root node
    // ({data, pagination}). Each item is {object, id, key, name, format}. The
    // property's ID — not its key — is what the tag-options endpoint addresses, so
    // a caller resolving a free-vocabulary select first reads this to map key→id.
    public async Task<JsonObject> ListPropertiesAsync(
        int offset = 0, int limit = 100, CancellationToken ct = default)
    {
        string path = $"{_spacePath}/properties?offset={offset}&limit={limit}";
        return await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
    }

    // GET a property's existing tag options (one page) → returns the root node
    // ({data, pagination}). Each item is {object, id, key, name, color}. The path
    // takes the property ID (the key form 404s, verified against the live API);
    // resolve the key to an id through ListPropertiesAsync first.
    public async Task<JsonObject> ListPropertyTagsAsync(
        string propertyId, int offset = 0, int limit = 100, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyId);
        string path = $"{_spacePath}/properties/{propertyId}/tags?offset={offset}&limit={limit}";
        return await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
    }

    // POST list members. A collection IS a list: members are added through
    // /lists/{id}/objects with body {objects:[ids]} (vendor addToCollection).
    // The success body is a bare JSON string, not an object — skip parsing it.
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

    // DELETE one list member without touching the object itself. Collection
    // membership is provider structure, separate from an objects relation.
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

    // Serialized send with one transient retry. Returns the parsed root object
    // (empty JsonObject when the body is empty). parseBody:false skips parsing
    // entirely, for endpoints whose success body is not a JSON object — list-add
    // answers a bare string. Throws HttpRequestException on a non-retryable or
    // retry-exhausted failure, after emitting ApiRequestFailed.
    private async Task<JsonObject> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        CancellationToken ct,
        bool parseBody = true)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                bool traceRequest = DeckleAnytypeSource.Log.IsEnabled(
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Network);
                long startTicks = traceRequest ? Stopwatch.GetTimestamp() : 0;
                if (traceRequest)
                    DeckleAnytypeSource.Log.ApiRequestStarted(method.Method, path);

                using var request = new HttpRequestMessage(method, path);
                if (body is not null)
                {
                    request.Content = new StringContent(
                        body.ToJsonString(), Encoding.UTF8, "application/json");
                }

                using HttpResponseMessage response =
                    await _http.SendAsync(request, ct).ConfigureAwait(false);

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

                if (attempt < MaxRetries && IsTransient(response.StatusCode))
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

                // Terminal failure. Surface the server message (it is API error
                // text, never key material) and throw.
                string errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                DeckleAnytypeSource.Log.ApiRequestFailed();
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

    private static bool IsTransient(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    // Retry-After is seconds (delta) or an HTTP-date. HttpClient parses both
    // into RetryConditionHeaderValue; Delta wins when present, otherwise the
    // date is turned into a delay from now (never negative).
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
