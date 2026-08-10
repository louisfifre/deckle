using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for TaskGestures over a loopback HttpListener fake (no
// existing HTTP test helper in tests/, so FakeAnytypeServer is the minimal local
// one — see its file). These assert the WIRE EFFECT of a gesture: the exact
// payload the API receives, not the digest string. The task selector is always a
// bafy* id so the resolver short-circuits and no /search route is needed.
[Trait("Category", "integration")]
public class TaskGesturesTests
{
    // A plausible Anytype object id: >40 chars and "bafy"-prefixed so the resolver
    // treats it as an id directly instead of searching.
    const string TaskId = "bafyreiTaskaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static TaskGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new TaskGestures(client, new NameResolver(client));
    }

    // GET response for the anchor task carrying the given markdown body.
    static JsonObject TaskObject(string markdown) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = TaskId,
            ["name"] = "Ma tâche",
            ["markdown"] = markdown,
            ["properties"] = new JsonArray(),
        },
    };

    // ── CreateAsync ─────────────────────────────────────────────────────────────

    // A bafy* project id so the resolver short-circuits (no /search route needed).
    const string ProjectId = "bafyreiprojectaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task CreatePassesTheTaskTemplateIdSoTheTaskIsBornFromItsTemplate()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostObject(TaskObject(""));

        await NewGestures(server).CreateAsync(ProjectId, "Ma tâche", type: "production", ct: Ct);

        // The API ignores the default template unless template_id is named; the
        // creation POST must carry the task type's frozen template id.
        JsonObject created = server.LastBodyFor("POST");
        Assert.Equal(DevSpace.Templates.Task, created["template_id"]!.GetValue<string>());
    }

    // ── SubtaskAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SubtaskSetFalseWithNoMatchAppendsAnUncheckedItemAtTheEnd()
    {
        using var server = new FakeAnytypeServer();
        string body = "# Notes\n\n- [ ] déjà là";
        server.OnGetObject(TaskId, TaskObject(body));
        server.OnPatchObject(TaskId, TaskObject(body)); // PATCH echoes an object

        await NewGestures(server).SubtaskAsync(TaskId, "nouvelle étape", done: false, ct: Ct);

        // The PATCH rewrites the whole markdown: original body preserved verbatim,
        // a new "- [ ] label" line tacked on at the end.
        JsonObject patched = server.LastBodyFor("PATCH");
        string next = patched["markdown"]!.GetValue<string>();
        Assert.Equal("# Notes\n\n- [ ] déjà là\n- [ ] nouvelle étape", next);
    }

    [Fact]
    public async Task SubtaskMatchesAnExistingItemCaseInsensitivelyAndChecksIt()
    {
        using var server = new FakeAnytypeServer();
        string body = "- [ ] Rédiger le BRIEF\n- [ ] relire";
        server.OnGetObject(TaskId, TaskObject(body));
        server.OnPatchObject(TaskId, TaskObject(body));

        // Label "brief" matches "Rédiger le BRIEF" by case-insensitive contains
        // (the differing-case portion is ASCII, so the fold is unambiguous).
        await NewGestures(server).SubtaskAsync(TaskId, "brief", done: true, ct: Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        string next = patched["markdown"]!.GetValue<string>();
        // First line toggled to [x]; the second line is untouched.
        Assert.Equal("- [x] Rédiger le BRIEF\n- [ ] relire", next);
    }

    [Fact]
    public async Task SubtaskWithDoneTrueOnAnAlreadyCheckedItemKeepsItChecked()
    {
        using var server = new FakeAnytypeServer();
        string body = "- [x] terminé\n- [ ] reste";
        server.OnGetObject(TaskId, TaskObject(body));
        server.OnPatchObject(TaskId, TaskObject(body));

        await NewGestures(server).SubtaskAsync(TaskId, "terminé", done: true, ct: Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        string next = patched["markdown"]!.GetValue<string>();
        // Idempotent: the checked item stays checked, the rest is verbatim.
        Assert.Equal("- [x] terminé\n- [ ] reste", next);
    }

    [Fact]
    public async Task SubtaskReplayKeepsOneItemInTheRequestedState()
    {
        using var server = new FakeAnytypeServer();
        string before = "# Notes";
        string after = "# Notes\n- [ ] relire";
        server.OnGetObject(TaskId, TaskObject(before));
        server.OnGetObject(TaskId, TaskObject(after));
        server.OnPatchObject(TaskId, TaskObject(after));
        server.OnPatchObject(TaskId, TaskObject(after));

        TaskGestures gestures = NewGestures(server);
        await gestures.SubtaskAsync(TaskId, "relire", done: false, ct: Ct);
        await gestures.SubtaskAsync(TaskId, "relire", done: false, ct: Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        Assert.Equal(after, patched["markdown"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task SubtaskRejectsABlankLabelBeforeReadingOrWriting()
    {
        using var server = new FakeAnytypeServer();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).SubtaskAsync(TaskId, "   ", done: false, ct: Ct));

        Assert.Contains("libellé", error.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task SubtaskRefusesAnAmbiguousPartialLabelWithoutWriting()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject("- [ ] Relire le brief\n- [ ] Relire le rapport"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).SubtaskAsync(TaskId, "relire", done: true, ct: Ct));

        Assert.Contains("ambiguë", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task SubtaskPreservesNonChecklistBodyVerbatim()
    {
        using var server = new FakeAnytypeServer();
        string body = "# Titre\nUn paragraphe libre.\n\n- [ ] une étape\nUne note finale.";
        server.OnGetObject(TaskId, TaskObject(body));
        server.OnPatchObject(TaskId, TaskObject(body));

        await NewGestures(server).SubtaskAsync(TaskId, "une étape", done: true, ct: Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        string next = patched["markdown"]!.GetValue<string>();
        // Only the checkbox glyph changes; every other line is identical.
        Assert.Equal("# Titre\nUn paragraphe libre.\n\n- [x] une étape\nUne note finale.", next);
    }

}

// ─── FakeAnytypeServer ─────────────────────────────────────────────────────────
//
// Minimal loopback HTTP server standing in for the local Anytype REST API. No
// HttpListener helper existed in tests/, so this is the minimal local one the
// integration tests share (TaskGesturesTests + SessionGesturesTests). It:
//   • binds an HttpListener on a free loopback port,
//   • routes a request to the first matching canned (method, path) response,
//   • records every received request (method, path, parsed JSON body) so a test
//     can assert on the exact payload the gesture sent.
//
// Routing is registration-order; OnGetObject/OnPostObject/etc. register the
// canned responses a given test needs. An unmatched request gets 404 — a test
// hitting an unexpected route fails loudly rather than hanging.
internal sealed class FakeAnytypeServer : IDisposable
{
    // Fixed space id so the path prefix is deterministic in assertions.
    public const string Space = "test-space";

    readonly LoopbackHttpListenerLease _listenerLease;
    readonly HttpListener _listener;
    readonly string _prefix;
    readonly List<Route> _routes = new();
    readonly ConcurrentDictionary<string, int> _routeHits = new();
    readonly ConcurrentQueue<Received> _received = new();
    readonly Task _loop;

    sealed record Route(string Method, string Path, int Status, string Json);
    public readonly record struct Received(string Method, string Path, string Body);

    public FakeAnytypeServer()
    {
        _listenerLease = LoopbackHttpListenerLease.Start();
        _prefix = _listenerLease.Prefix;
        _listener = _listenerLease.Listener;
        _loop = Task.Run(ServeAsync);
    }

    public AnytypeCredentials Credentials =>
        new(_prefix.TrimEnd('/'), "2025-11-08", "test-key", Space);

    string SpacePath => $"/v1/spaces/{Space}";

    // ── Route registration ────────────────────────────────────────────────────

    public void OnGetObject(string id, JsonObject response) =>
        _routes.Add(new("GET", $"{SpacePath}/objects/{id}", 200, response.ToJsonString()));

    public void OnPatchObject(string id, JsonObject response, int status = 200) =>
        _routes.Add(new("PATCH", $"{SpacePath}/objects/{id}", status, response.ToJsonString()));

    public void OnPostObject(JsonObject response, int status = 200) =>
        _routes.Add(new("POST", $"{SpacePath}/objects", status, response.ToJsonString()));

    public void OnListTypes(JsonObject response) =>
        _routes.Add(new("GET", $"{SpacePath}/types", 200, response.ToJsonString()));

    public void OnPostType(JsonObject response) =>
        _routes.Add(new("POST", $"{SpacePath}/types", 201, response.ToJsonString()));

    public void OnPatchType(string id, JsonObject response) =>
        _routes.Add(new("PATCH", $"{SpacePath}/types/{id}", 200, response.ToJsonString()));

    public void OnPostProperty(JsonObject response) =>
        _routes.Add(new("POST", $"{SpacePath}/properties", 201, response.ToJsonString()));

    public void OnDeleteObject(string id, JsonObject response) =>
        _routes.Add(new("DELETE", $"{SpacePath}/objects/{id}", 200, response.ToJsonString()));

    public void OnPostChat(JsonObject response) =>
        _routes.Add(new("POST", $"{SpacePath}/chats", 201, response.ToJsonString()));

    public void OnGetChatMessages(string chatId, JsonObject response) =>
        _routes.Add(new("GET", $"{SpacePath}/chats/{chatId}/messages", 200, response.ToJsonString()));

    public void OnPostChatMessage(string chatId, JsonObject response) =>
        _routes.Add(new("POST", $"{SpacePath}/chats/{chatId}/messages", 201, response.ToJsonString()));

    public void OnSearch(JsonObject response) =>
        _routes.Add(new("POST", $"{SpacePath}/search", 200, response.ToJsonString()));

    // Raw JSON payload for the list-add route — the live endpoint answers a bare
    // JSON string, which a JsonObject-based registration could not express.
    public void OnPostListObjects(string listId, string rawJson) =>
        _routes.Add(new("POST", $"{SpacePath}/lists/{listId}/objects", 200, rawJson));

    // GET .../properties — the space's property list (key→id map source). The
    // serve loop routes on AbsolutePath, so the ?offset&limit query is ignored.
    public void OnListProperties(JsonObject response) =>
        _routes.Add(new("GET", $"{SpacePath}/properties", 200, response.ToJsonString()));

    // GET .../properties/{id}/tags — a property's existing options. Same path-only
    // routing; the query string is dropped before the match.
    public void OnListPropertyTags(string propertyId, JsonObject response) =>
        _routes.Add(new("GET", $"{SpacePath}/properties/{propertyId}/tags", 200, response.ToJsonString()));

    public void OnPostPropertyTag(string propertyId, JsonObject response) =>
        _routes.Add(new("POST", $"{SpacePath}/properties/{propertyId}/tags", 201, response.ToJsonString()));

    // ── Request introspection ─────────────────────────────────────────────────

    // The parsed JSON body of the last request with the given method. Throws if
    // no such request was recorded — a missing call is a test failure, not null.
    public JsonObject LastBodyFor(string method)
    {
        Received last = _received.Where(r => r.Method == method).LastOrDefault();
        if (last.Method is null)
            throw new InvalidOperationException($"No {method} request was received.");
        return (JsonObject)JsonNode.Parse(last.Body)!;
    }

    public IReadOnlyList<Received> Requests => _received.ToArray();

    // ── Serve loop ────────────────────────────────────────────────────────────

    async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; } // listener stopped

            string method = ctx.Request.HttpMethod;
            string path = ctx.Request.Url!.AbsolutePath;
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();
            _received.Enqueue(new Received(method, path, body));

            Route? route = NextRoute(method, path);
            byte[] payload = Encoding.UTF8.GetBytes(route?.Json ?? "{}");
            ctx.Response.StatusCode = route?.Status ?? 404;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        }
    }

    Route? NextRoute(string method, string path)
    {
        Route[] matches = _routes.Where(r => r.Method == method && r.Path == path).ToArray();
        if (matches.Length == 0) return null;

        string key = method + " " + path;
        int hit = _routeHits.AddOrUpdate(key, 1, (_, count) => count + 1);
        return matches[Math.Min(hit - 1, matches.Length - 1)];
    }

    public void Dispose()
    {
        _listenerLease.Dispose();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}
