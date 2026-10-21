using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for TaskGestures over FakeAnytypeServer (below). These
// assert the WIRE EFFECT of a gesture: the exact payload the API receives, not
// the digest string. Selectors are always bafy* ids so the resolver
// short-circuits and no /search route is needed.
[Trait("Category", "integration")]
public class TaskGesturesTests
{
    const string TaskId = "bafyreiTaskaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static TaskGestures NewGestures(FakeAnytypeServer server, FakeV2Handler? v2 = null)
    {
        var client = new AnytypeApiClient(server.Credentials);
        var v2Client = new AnytypeV2Client(
            new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"),
            null,
            v2 ?? new FakeV2Handler());
        return new TaskGestures(client, new NameResolver(client), v2Client);
    }

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

    const string ProjectId = "bafyreiprojectaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task CreatePassesTheTaskTemplateIdSoTheTaskIsBornFromItsTemplate()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostObject(TaskObject(""));

        await NewGestures(server).CreateAsync(ProjectId, "Ma tâche", type: "production", ct: Ct);

        JsonObject created = server.LastBodyFor("POST");
        Assert.Equal(DevSpace.Templates.Task, created["template_id"]!.GetValue<string>());
        // No body on the POST: it would land under the template's last section.
        Assert.False(created.ContainsKey("body"));
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // A checklist or notes passed at creation fill the template's own sections,
    // written as a second step once the template has shaped the object.
    [Fact]
    [Trait("Category", "regression")]
    public async Task CreateWritesChecklistAndNotesIntoTheTemplateSections()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostObject(TaskObject(""));
        server.OnPatchObject(TaskId, TaskObject(""));

        string digest = await NewGestures(server).CreateAsync(
            ProjectId, "Ma tâche", type: "production",
            checklist: ["relire", "livrer"], notes: "Attention au cache.", ct: Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        Assert.Equal(
            $"## {ReadGestures.ChecklistHeading}\n- [ ] relire\n- [ ] livrer\n\n## {ReadGestures.NotesHeading}\nAttention au cache.",
            patched["markdown"]!.GetValue<string>());
        Assert.Contains($"id : {TaskId}", digest);
    }

    // ── SubtaskAsync ──────────────────────────────────────────────────────────
    //
    // task_subtask edits the checklist through the v2 client (over
    // FakeV2Handler): one read of the document, then one PATCH of block ops.
    // The v1 server is only there for the name resolver, which a bafy* id
    // short-circuits, so a subtask test expects it to see no request at all.

    const string TaskPath = "/v2/spaces/space-1/objects/" + TaskId;
    const string ETag = "\"e1\"";

    static JsonObject Block(string id, string type, string? text = null, bool? @checked = null, int indent = 0)
    {
        var block = new JsonObject { ["id"] = id, ["type"] = type, ["indent"] = indent };
        if (text is not null) block["text"] = text;
        if (@checked is { } ticked) block["checked"] = ticked;
        return block;
    }

    // A v2 object read: the title as a property and the blocks in reading order.
    static string V2Task(params JsonObject[] blocks) => new JsonObject
    {
        ["properties"] = new JsonObject { ["name"] = "Ma tâche" },
        ["blocks"] = new JsonArray(blocks.Select(b => (JsonNode)b.DeepClone()).ToArray()),
    }.ToJsonString();

    // The task template as the survey measured it: the checklist heading, a
    // divider, one empty checkbox, then the notes section.
    static JsonObject[] TemplateBlocks() =>
    [
        Block("h1", "heading_2", ReadGestures.ChecklistHeading),
        Block("d1", "divider"),
        Block("slot", "checkbox"),
        Block("d2", "divider"),
        Block("h2", "heading_2", ReadGestures.NotesHeading),
        Block("n1", "paragraph", "Une note."),
    ];

    static FakeV2Handler ReadThenAccept(string readJson, string? etag = ETag)
    {
        var handler = new FakeV2Handler();
        if (etag is null) handler.Reply(HttpStatusCode.OK, readJson);
        else handler.Reply(HttpStatusCode.OK, readJson, ("ETag", etag));
        return handler.Reply(HttpStatusCode.OK, "{}");
    }

    // The single PATCH the gesture sent, as its parsed ops array.
    static JsonArray SentOps(FakeV2Handler handler)
    {
        FakeV2Handler.Seen patch = Assert.Single(handler.Requests, r => r.Method == "PATCH");
        Assert.Equal(TaskPath, patch.PathAndQuery);
        return (JsonArray)JsonNode.Parse(patch.Body!)!["ops"]!;
    }

    static JsonObject OnlyOp(FakeV2Handler handler) => (JsonObject)Assert.Single(SentOps(handler))!;

    static FakeV2Handler.Seen SentPatch(FakeV2Handler handler)
        => Assert.Single(handler.Requests, r => r.Method == "PATCH");

    // A match is one update_block that sets the exact requested state on the
    // block named by its id: a set, never a toggle, so it is the same call
    // whichever way the item stood. It depends on nothing else the read saw,
    // so it carries no If-Match.
    [Theory]
    [InlineData(true, "Sous-tâche cochée dans Ma tâche : brief")]
    [InlineData(false, "Sous-tâche décochée dans Ma tâche : brief")]
    public async Task SubtaskSetsTheExactStateOnTheMatchedCheckboxWithOneUpdateBlock(bool done, string answer)
    {
        using var server = new FakeAnytypeServer();
        var v2 = ReadThenAccept(V2Task(
            Block("a1", "checkbox", "Rédiger le BRIEF", @checked: true),
            Block("a2", "checkbox", "relire")));

        string response = await NewGestures(server, v2).SubtaskAsync(TaskId, "brief", done, Ct);

        JsonObject op = OnlyOp(v2);
        Assert.Equal("update_block", op["op"]!.GetValue<string>());
        Assert.Equal("a1", op["id"]!.GetValue<string>());
        Assert.Equal(done, op["set"]!["checked"]!.GetValue<bool>());
        Assert.Single(op["set"]!.AsObject());
        Assert.False(SentPatch(v2).HasHeader("If-Match"));
        Assert.Equal(answer, response);
        // Nothing went through v1: the whole gesture is the read and the write.
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task SubtaskWithDoneTrueOnAnAlreadyCheckedItemSendsTheSameSet()
    {
        using var server = new FakeAnytypeServer();
        var v2 = ReadThenAccept(V2Task(Block("a1", "checkbox", "terminé", @checked: true)));

        await NewGestures(server, v2).SubtaskAsync(TaskId, "terminé", done: true, ct: Ct);

        JsonObject op = OnlyOp(v2);
        Assert.Equal("a1", op["id"]!.GetValue<string>());
        Assert.True(op["set"]!["checked"]!.GetValue<bool>());
    }

    // Anytype escapes markdown in the text it serves (`get\_task` for
    // get_task). Matching the raw label never found an item with an
    // underscore and added a ticked duplicate instead.
    [Fact]
    [Trait("Category", "regression")]
    public async Task SubtaskMatchesAnEscapedLabelTypedNaturally()
    {
        using var server = new FakeAnytypeServer();
        var v2 = ReadThenAccept(V2Task(
            Block("h1", "heading_2", ReadGestures.ChecklistHeading),
            Block("a1", "checkbox", "dessiner get\\_task\\_properties")));

        await NewGestures(server, v2).SubtaskAsync(TaskId, "get_task_properties", done: true, ct: Ct);

        JsonObject op = OnlyOp(v2);
        Assert.Equal("update_block", op["op"]!.GetValue<string>());
        Assert.Equal("a1", op["id"]!.GetValue<string>());
        Assert.True(op["set"]!["checked"]!.GetValue<bool>());
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task SubtaskRefusesAnAmbiguousPartialLabelWithoutWriting()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler().Reply(HttpStatusCode.OK, V2Task(
            Block("a1", "checkbox", "Relire le brief"),
            Block("a2", "checkbox", "Relire le rapport")));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, v2).SubtaskAsync(TaskId, "relire", done: true, ct: Ct));

        Assert.Contains("ambiguë", error.Message);
        Assert.DoesNotContain(v2.Requests, request => request.Method == "PATCH");
    }

    // The checklist is not always at the top level: the template can hold its
    // checkboxes inside a row and its columns (indent 2 is measured).
    [Fact]
    [Trait("Category", "regression")]
    public async Task SubtaskFindsACheckboxNestedInsideColumns()
    {
        using var server = new FakeAnytypeServer();
        var v2 = ReadThenAccept(V2Task(
            Block("r1", "row"),
            Block("c1", "column", indent: 1),
            Block("a1", "checkbox", "dans la colonne", indent: 2),
            Block("c2", "column", indent: 1),
            Block("a2", "checkbox", "autre colonne", indent: 2)));

        await NewGestures(server, v2).SubtaskAsync(TaskId, "autre colonne", done: true, ct: Ct);

        JsonObject op = OnlyOp(v2);
        Assert.Equal("update_block", op["op"]!.GetValue<string>());
        Assert.Equal("a2", op["id"]!.GetValue<string>());
    }

    // The task template ships an empty checkbox under its checklist heading;
    // the first subtask fills it instead of leaving it behind. The fill depends
    // on the slot still being empty, so it carries the ETag of the read.
    [Fact]
    [Trait("Category", "regression")]
    public async Task SubtaskFillsTheTemplatesEmptySlotBeforeAddingABlock()
    {
        using var server = new FakeAnytypeServer();
        var v2 = ReadThenAccept(V2Task(TemplateBlocks()));

        string response = await NewGestures(server, v2).SubtaskAsync(TaskId, "première", done: false, ct: Ct);

        JsonObject op = OnlyOp(v2);
        Assert.Equal("update_block", op["op"]!.GetValue<string>());
        Assert.Equal("slot", op["id"]!.GetValue<string>());
        Assert.Equal("première", op["set"]!["text"]!.GetValue<string>());
        Assert.False(op["set"]!["checked"]!.GetValue<bool>());
        Assert.Equal(ETag, SentPatch(v2).Header("If-Match"));
        Assert.Equal("Sous-tâche ajoutée à Ma tâche : première", response);
    }

    // A new item joins the checklist after its last item, never the end of the
    // body, where it would sit under the notes section. A paragraph of the
    // notes that happens to read like the label is not an item.
    [Fact]
    [Trait("Category", "regression")]
    public async Task SubtaskWithNoMatchFollowsTheLastItemNotTheNotes()
    {
        using var server = new FakeAnytypeServer();
        var v2 = ReadThenAccept(V2Task(
            Block("h1", "heading_2", ReadGestures.ChecklistHeading),
            Block("a1", "checkbox", "une", @checked: true),
            Block("a2", "checkbox", "encore une"),
            Block("h2", "heading_2", ReadGestures.NotesHeading),
            Block("n1", "paragraph", "deux")));

        await NewGestures(server, v2).SubtaskAsync(TaskId, "deux", done: true, ct: Ct);

        JsonObject op = OnlyOp(v2);
        Assert.Equal("insert_blocks", op["op"]!.GetValue<string>());
        Assert.Equal("a2", op["after"]!.GetValue<string>());
        Assert.False(op.ContainsKey("position"));
        JsonObject block = (JsonObject)Assert.Single(op["blocks"]!.AsArray())!;
        Assert.Equal("checkbox", block["type"]!.GetValue<string>());
        Assert.Equal("deux", block["text"]!.GetValue<string>());
        Assert.True(block["checked"]!.GetValue<bool>());
        Assert.Equal(ETag, SentPatch(v2).Header("If-Match"));
    }

    // No item and no empty slot, but the checklist heading is there: the new
    // item goes right after it, not at the end of the body.
    [Fact]
    public async Task SubtaskWithNoMatchFollowsTheChecklistHeadingWhenThereIsNoItem()
    {
        using var server = new FakeAnytypeServer();
        var v2 = ReadThenAccept(V2Task(
            Block("p0", "paragraph", "Intro."),
            Block("h1", "heading_2", ReadGestures.ChecklistHeading + " "),
            Block("h2", "heading_2", ReadGestures.NotesHeading),
            Block("n1", "paragraph", "Une note.")));

        await NewGestures(server, v2).SubtaskAsync(TaskId, "première", done: false, ct: Ct);

        JsonObject op = OnlyOp(v2);
        Assert.Equal("insert_blocks", op["op"]!.GetValue<string>());
        Assert.Equal("h1", op["after"]!.GetValue<string>());
        Assert.False(op["blocks"]![0]!["checked"]!.GetValue<bool>());
        Assert.Equal(ETag, SentPatch(v2).Header("If-Match"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubtaskWithNoChecklistAtAllAppendsAtTheEnd(bool blocksKeyPresent)
    {
        using var server = new FakeAnytypeServer();
        // An empty body, with or without a blocks array, and a body that has
        // blocks but no checklist, both end up at the end of the document.
        string read = blocksKeyPresent
            ? V2Task(Block("p0", "paragraph", "Du texte."))
            : "{\"properties\":{\"name\":\"Ma tâche\"}}";
        var v2 = ReadThenAccept(read);

        await NewGestures(server, v2).SubtaskAsync(TaskId, "première", done: false, ct: Ct);

        JsonObject op = OnlyOp(v2);
        Assert.Equal("insert_blocks", op["op"]!.GetValue<string>());
        Assert.Equal("last", op["position"]!.GetValue<string>());
        Assert.False(op.ContainsKey("after"));
        Assert.Equal("première", op["blocks"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(ETag, SentPatch(v2).Header("If-Match"));
    }

    // The condition is the ETag of the read: a read that gave none sends none.
    [Fact]
    public async Task SubtaskSendsNoIfMatchWhenTheReadGaveNoETag()
    {
        using var server = new FakeAnytypeServer();
        var v2 = ReadThenAccept(V2Task(TemplateBlocks()), etag: null);

        await NewGestures(server, v2).SubtaskAsync(TaskId, "première", done: false, ct: Ct);

        Assert.False(SentPatch(v2).HasHeader("If-Match"));
    }

    // A replay of the same call finds the item the first one added and sets the
    // same state on it: one item, in the requested state.
    [Fact]
    public async Task SubtaskReplayKeepsOneItemInTheRequestedState()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Task(Block("h1", "heading_2", ReadGestures.ChecklistHeading)), ("ETag", ETag))
            .Reply(HttpStatusCode.OK, "{}")
            .Reply(HttpStatusCode.OK, V2Task(
                Block("h1", "heading_2", ReadGestures.ChecklistHeading),
                Block("a1", "checkbox", "relire")), ("ETag", "\"e2\""))
            .Reply(HttpStatusCode.OK, "{}");

        TaskGestures gestures = NewGestures(server, v2);
        await gestures.SubtaskAsync(TaskId, "relire", done: false, ct: Ct);
        await gestures.SubtaskAsync(TaskId, "relire", done: false, ct: Ct);

        var patches = v2.Requests.Where(r => r.Method == "PATCH").ToArray();
        Assert.Equal(2, patches.Length);
        var second = (JsonObject)((JsonArray)JsonNode.Parse(patches[1].Body!)!["ops"]!).Single()!;
        Assert.Equal("update_block", second["op"]!.GetValue<string>());
        Assert.Equal("a1", second["id"]!.GetValue<string>());
        Assert.False(second["set"]!["checked"]!.GetValue<bool>());
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task SubtaskRejectsABlankLabelBeforeReadingOrWriting()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server, v2).SubtaskAsync(TaskId, "   ", done: false, ct: Ct));

        Assert.Contains("libellé", error.Message);
        Assert.Empty(server.Requests);
        Assert.Empty(v2.Requests);
    }
}

// Minimal loopback HTTP server standing in for the local Anytype REST API,
// shared by the integration tests. It:
//   • listens on a loopback port leased from LoopbackHttpListenerLease,
//   • answers the n-th request to a (method, path) with the n-th canned
//     response registered for it, repeating the last one,
//   • records every received request (method, path, query, body) so a test
//     can assert on the exact payload the gesture sent.
//
// An unmatched request gets 404 — a test hitting an unexpected route fails
// loudly rather than hanging.
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
    // Query is the raw query string ("?offset=0&limit=1000"), empty when absent.
    public readonly record struct Received(string Method, string Path, string Query, string Body);

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

    // The serve loop routes on AbsolutePath, so the ?offset&limit query is
    // ignored.
    public void OnListProperties(JsonObject response) =>
        _routes.Add(new("GET", $"{SpacePath}/properties", 200, response.ToJsonString()));

    public void OnListPropertyTags(string propertyId, JsonObject response) =>
        _routes.Add(new("GET", $"{SpacePath}/properties/{propertyId}/tags", 200, response.ToJsonString()));

    public void OnPostPropertyTag(string propertyId, JsonObject response) =>
        _routes.Add(new("POST", $"{SpacePath}/properties/{propertyId}/tags", 201, response.ToJsonString()));

    // ── Request introspection ─────────────────────────────────────────────────

    // A missing call is a test failure, not null.
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
            Uri url = ctx.Request.Url!;
            string path = url.AbsolutePath;
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();
            _received.Enqueue(new Received(method, path, url.Query, body));

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
