using System.Net;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for SessionGestures over the shared FakeAnytypeServer
// (defined in TaskGesturesTests.cs). A session report carries the journal date
// and the anchor task in « Tâche(s) liée(s) » (the report has no project
// property); session_touch appends a further task to an explicit report; a
// follow-up log appends blocks to the report body. open_report and
// session_log talk v2 (over FakeV2Handler); session_touch still talks v1.
[Trait("Category", "integration")]
public class SessionGesturesTests
{
    const string TaskId      = "bafyreiTaskbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string OtherTaskId = "bafyreiTask2bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string ProjectId   = "bafyreiProjccccccccccccccccccccccccccccccccccccccccccc";
    const string NewReport   = "bafyreiNewdddddddddddddddddddddddddddddddddddddddddddddd";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static SessionGestures NewGestures(
        FakeAnytypeServer server, FakeV2Handler? v2 = null, TimeProvider? time = null)
    {
        var client = new AnytypeApiClient(server.Credentials);
        var v2Client = new AnytypeV2Client(
            new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"),
            null,
            v2 ?? new FakeV2Handler());
        return new SessionGestures(client, new NameResolver(client), v2Client, time);
    }

    // A clock stopped at 2026-06-12 14:05 whose local zone is UTC, so the
    // stamp does not depend on the zone of the machine running the tests.
    sealed class StoppedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 6, 12, 14, 5, 30, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    const string LogPath = "/v2/spaces/space-1/objects/" + NewReport;

    // What a v2 object read answers for the log's read: an outline holding, unless
    // the body is empty, the « # Journal » heading block a fresh v2 report opens
    // on, then one block per type given.
    static string V2Report(bool withBlock, params string[] moreBlockTypes)
    {
        var blocks = new JsonArray();
        if (withBlock) blocks.Add(new JsonObject { ["id"] = "h1", ["type"] = "heading_1" });
        for (int i = 0; i < moreBlockTypes.Length; i++)
            blocks.Add(new JsonObject { ["id"] = $"b{i}", ["type"] = moreBlockTypes[i] });
        return "{\"properties\":{},\"outline\":" + blocks.ToJsonString() + "}";
    }

    // The single PATCH the log sends, as its parsed ops array.
    static JsonArray LoggedOps(FakeV2Handler handler)
    {
        FakeV2Handler.Seen patch = Assert.Single(handler.Requests, r => r.Method == "PATCH");
        Assert.Equal(LogPath, patch.PathAndQuery);
        return (JsonArray)JsonNode.Parse(patch.Body!)!["ops"]!;
    }

    static JsonObject TaskObject(string id, string name) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["markdown"] = "",
            ["properties"] = new JsonArray(new JsonObject
            {
                ["key"] = DevSpace.Props.Projects,
                ["objects"] = new JsonArray(ProjectId),
            }),
        },
    };

    static JsonObject ReportObject(string id, string markdown, params string[] taskIds) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = id,
            ["markdown"] = markdown,
            ["properties"] = new JsonArray(
                new JsonObject { ["key"] = DevSpace.Props.Tasks, ["objects"] = Refs(taskIds) }),
        },
    };

    static JsonArray Refs(string[] ids)
    {
        var array = new JsonArray();
        foreach (string id in ids) array.Add(id);
        return array;
    }

    // The v2 answers open_report's requests, in the order they are sent: the
    // task's properties, one properties read per linked project, then the
    // report create. Every read comes before the write.
    static string V2Named(string id, string type, string name, string extraProperties = "") =>
        "{\"id\":\"" + id + "\",\"type\":\"" + type + "\",\"properties\":{\"name\":\"" + name + "\""
        + extraProperties + "}}";

    static FakeV2Handler WireOpenReads(string? createAnswer = null, string taskType = "task") =>
        new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Named(TaskId, taskType, "Tâche d'ancrage",
                ",\"" + DevSpace.Props.Projects + "\":[\"" + ProjectId + "\"]"))
            .Reply(HttpStatusCode.OK, V2Named(ProjectId, "project", "Mon projet"))
            // The shape of a real create's answer (measured): id and etag at the root.
            .Reply(HttpStatusCode.Created,
                createAnswer ?? "{\"id\":\"" + NewReport + "\",\"type\":\"rapport\",\"etag\":\"1ea04e49\"}");

    // ── close_report ─────────────────────────────────────────────────────────

    const string OldReport = "bafyreiOldeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    static string Json(JsonObject o) => o.ToJsonString();

    static string SearchOf(params (string Id, string[] Tasks)[] reports)
    {
        var data = new JsonArray();
        foreach (var (id, tasks) in reports)
            data.Add(new JsonObject
            {
                ["id"] = id, ["name"] = "", ["type"] = "report",
                ["properties"] = new JsonObject { [DevSpace.Props.Tasks] = Refs(tasks) },
            });
        return Json(new JsonObject { ["data"] = data, ["has_more"] = false });
    }

    static string Markdown(string md) => Json(new JsonObject { ["markdown"] = md });

    // The clock stops at 14:05: 09:00 to 10:00 counts, 10:00 waits on the
    // person, 13:00 to 14:05 counts: 2 h 05, written 2,08 h.
    const string Journal =
        "# Journal 2026-06-12\n\n2026-06-12 09:00\n\nDébut\n\n --- \n\n"
        + "2026-06-12 10:00\n\nLivré, en attente de retour\n\n --- \n\n2026-06-12 13:00\n\nReprise";

    [Fact]
    public async Task CloseReportWritesTheClosingEntryTicksTheTasksAndSumsTheirRealTime()
    {
        using var server = new FakeAnytypeServer();
        server.OnPatchObject(TaskId, TaskObject(TaskId, "Tâche d'ancrage"));
        server.OnPatchObject(OtherTaskId, TaskObject(OtherTaskId, "Autre"));
        FakeV2Handler v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Named(NewReport, "report", "",
                ",\"" + DevSpace.Props.Tasks + "\":[\"" + TaskId + "\",\"" + OtherTaskId + "\"]"))
            .Reply(HttpStatusCode.OK, Markdown(Journal))
            .Reply(HttpStatusCode.OK, V2Report(true, "paragraph"))
            .Reply(HttpStatusCode.OK, "{}")
            // The anchor task: this report, shared with the other task, and an
            // older one closed alone at 1,5 h.
            .Reply(HttpStatusCode.OK, SearchOf((NewReport, [TaskId, OtherTaskId]), (OldReport, [TaskId])))
            .Reply(HttpStatusCode.OK, Markdown("# Journal 2026-06-10\n\n2026-06-10 18:00\n\n"
                + ReportTime.ClosingLine(1.5, "fin", corrected: false)))
            .Reply(HttpStatusCode.OK, V2Named(TaskId, "task", "Tâche d'ancrage"))
            .Reply(HttpStatusCode.OK, SearchOf((NewReport, [TaskId, OtherTaskId])))
            .Reply(HttpStatusCode.OK, V2Named(OtherTaskId, "task", "Autre"));

        string answer = await NewGestures(server, v2, new StoppedClock())
            .CloseReportAsync(NewReport, "Fini pour aujourd'hui.", done: [TaskId], ct: Ct);

        string logged = LoggedOps(v2)[0]!["markdown"]!.GetValue<string>();
        Assert.Contains("Clôture · temps réel 2,08 h. Fini pour aujourd'hui.", logged);

        // The tick, then each task's real time, summed: 2,08 / 2 + 1,5.
        var patches = server.Requests.Where(r => r.Method == "PATCH").ToList();
        Assert.Contains(patches, p => p.Path.EndsWith(TaskId) && p.Body.Contains("\"done\"") && p.Body.Contains("true"));
        Assert.DoesNotContain(patches, p => p.Path.EndsWith(OtherTaskId) && p.Body.Contains("\"done\""));
        Assert.Equal(2.54, RealHours(patches, TaskId));
        Assert.Equal(1.04, RealHours(patches, OtherTaskId));

        Assert.StartsWith("Rapport clos : temps réel 2,08 h", answer, StringComparison.Ordinal);
        Assert.Contains("[x] Tâche d'ancrage · temps réel 2,54 h", answer);
        Assert.Contains("[ ] Autre · temps réel 1,04 h", answer);
    }

    static double RealHours(IEnumerable<FakeAnytypeServer.Received> patches, string taskId)
    {
        FakeAnytypeServer.Received patch = Assert.Single(patches, p => p.Path.EndsWith(taskId)
            && p.Body.Contains(DevSpace.Props.TimeActual));
        JsonObject entry = (JsonObject)JsonNode.Parse(patch.Body)!["properties"]![0]!;
        return entry["number"]!.GetValue<double>();
    }

    [Fact]
    public async Task CloseReportTakesACorrectedFigureInsteadOfTheComputedOne()
    {
        using var server = new FakeAnytypeServer();
        server.OnPatchObject(TaskId, TaskObject(TaskId, "Tâche d'ancrage"));
        FakeV2Handler v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Named(NewReport, "report", "",
                ",\"" + DevSpace.Props.Tasks + "\":[\"" + TaskId + "\"]"))
            .Reply(HttpStatusCode.OK, Markdown(Journal))
            .Reply(HttpStatusCode.OK, V2Report(true, "paragraph"))
            .Reply(HttpStatusCode.OK, "{}")
            .Reply(HttpStatusCode.OK, SearchOf((NewReport, [TaskId])))
            .Reply(HttpStatusCode.OK, V2Named(TaskId, "task", "Tâche d'ancrage"));

        await NewGestures(server, v2, new StoppedClock())
            .CloseReportAsync(NewReport, "Fini.", correctedHours: 1, ct: Ct);

        Assert.Contains("temps réel 1 h (corrigé). Fini.", LoggedOps(v2)[0]!["markdown"]!.GetValue<string>());
        Assert.Equal(1, RealHours(server.Requests.Where(r => r.Method == "PATCH"), TaskId));
    }

    [Fact]
    public async Task CloseReportRefusesToTickATaskTheReportDoesNotCarryBeforeWriting()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Named(NewReport, "report", "",
                ",\"" + DevSpace.Props.Tasks + "\":[\"" + TaskId + "\"]"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, v2, new StoppedClock())
                .CloseReportAsync(NewReport, "Fini.", done: [OtherTaskId], ct: Ct));

        Assert.Contains("n'est pas une tâche de ce rapport", error.Message);
        Assert.DoesNotContain(v2.Requests, r => r.Method == "PATCH");
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task OpenReportCreatesAReportHeadedByTodayAndLinkedToTheAnchorTask()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = WireOpenReads();

        string answer = await NewGestures(server, v2, new StoppedClock()).OpenReportAsync(TaskId, Ct);

        Assert.StartsWith($"report_id : {NewReport}\n", answer, StringComparison.Ordinal);

        JsonObject create = CreateBody(v2);
        Assert.Equal(DevSpace.Types.Report, create["type"]!.GetValue<string>());
        // The heading opens the body; a report is a note and is sent no name,
        // which would land as a paragraph ahead of it.
        Assert.Equal("# Journal 2026-06-12", create["markdown"]!.GetValue<string>());
        Assert.False(create.ContainsKey("name"));

        var props = (JsonObject)create["properties"]!;
        // The model v6 has no journal-date property: the date is the heading's
        // (the stopped clock's day) and the object's native creation date.
        Assert.Equal([DevSpace.Props.Tasks], props.Select(p => p.Key));

        Assert.Equal(TaskId, ((JsonArray)props[DevSpace.Props.Tasks]!).Single()!.GetValue<string>());

        // The report has NO project property: its project is derived through the
        // linked task(s).
        Assert.False(props.ContainsKey(DevSpace.Props.Projects));

        // The create carries an Idempotency-Key; that the server dedupes a
        // resend on it is the writer's contract, not asserted here.
        FakeV2Handler.Seen post = CreateRequest(v2);
        Assert.False(string.IsNullOrWhiteSpace(post.Header("Idempotency-Key")));
    }

    // Every later log is addressed by this id: a create answer without one is
    // a failure, not an empty report_id handed to the caller.
    [Fact]
    public async Task OpenReportFailsWhenTheCreateAnswerCarriesNoId()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = WireOpenReads(createAnswer: "{\"type\":\"rapport\"}");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, v2).OpenReportAsync(TaskId, Ct));

        Assert.Contains("n'a pas d'id", error.Message);
    }

    // The answer is the handle and one line to check the task against: no
    // checklist and no tail of past reports (the digest that ran to 8 000
    // characters). Three requests: the task, its project, the create.
    [Fact]
    public async Task OpenReportAnswersWithTheIdAndOneLineNamingTheTaskAndItsProject()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = WireOpenReads();

        string answer = await NewGestures(server, v2).OpenReportAsync(TaskId, Ct);

        Assert.Equal($"report_id : {NewReport}\nTâche d'ancrage · Mon projet", answer);
        Assert.Equal(["GET", "GET", "POST"], v2.Requests.Select(r => r.Method));
        Assert.DoesNotContain(v2.Requests, r => r.PathAndQuery.Contains("/search"));
        // Nothing of open_report goes through v1.
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task OpenReportNamesADashWhenTheTaskHasNoProject()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Named(TaskId, "task", "Tâche d'ancrage"))
            .Reply(HttpStatusCode.Created, "{\"id\":\"" + NewReport + "\"}");

        string answer = await NewGestures(server, v2).OpenReportAsync(TaskId, Ct);

        Assert.Equal($"report_id : {NewReport}\nTâche d'ancrage · —", answer);
    }

    // The project name is a check for the person, not part of the report: a
    // project read that fails still opens the report on its task.
    [Fact]
    [Trait("Category", "regression")]
    public async Task OpenReportStillOpensWhenTheProjectCannotBeRead()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Named(TaskId, "task", "Tâche d'ancrage",
                ",\"" + DevSpace.Props.Projects + "\":[\"" + ProjectId + "\"]"))
            .Reply(HttpStatusCode.NotFound, "{\"message\":\"object not found\"}")
            .Reply(HttpStatusCode.Created, "{\"id\":\"" + NewReport + "\"}");

        string answer = await NewGestures(server, v2).OpenReportAsync(TaskId, Ct);

        Assert.Equal($"report_id : {NewReport}\nTâche d'ancrage · (projet non lu)", answer);
        Assert.Single(v2.Requests, r => r.Method == "POST");
    }

    // Like log with a report: a name is no stable anchor for a write, so only
    // an id is taken, and a name is refused before any request.
    [Fact]
    [Trait("Category", "regression")]
    public async Task OpenReportRequiresTheTaskIdBeforeReadingOrWriting()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server, v2).OpenReportAsync("Tâche d'ancrage", Ct));

        Assert.Contains("id Anytype stable", error.Message);
        Assert.Empty(server.Requests);
        Assert.Empty(v2.Requests);
    }

    // Resolving by id no longer filters by type, so the gesture checks it: a
    // report linked to a project or an idea has no place in the space.
    [Fact]
    [Trait("Category", "regression")]
    public async Task OpenReportRefusesAnObjectThatIsNotATaskBeforeCreating()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = WireOpenReads(taskType: "idea");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, v2).OpenReportAsync(TaskId, Ct));

        Assert.Contains("pas une tâche", error.Message);
        Assert.DoesNotContain(v2.Requests, r => r.Method == "POST");
    }

    [Fact]
    public async Task TouchAppendsTheTaskToTheCurrentReportsLinkedTasks()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(OtherTaskId, TaskObject(OtherTaskId, "Autre tâche"));
        server.OnGetObject(NewReport, ReportObject(NewReport, "# Journal", TaskId));
        server.OnPatchObject(NewReport, ReportObject(NewReport, "# Journal", TaskId, OtherTaskId));

        var gestures = NewGestures(server);
        await gestures.TouchTaskAsync(NewReport, OtherTaskId, Ct);

        JsonObject patch = server.LastBodyFor("PATCH");
        var props = (JsonArray)patch["properties"]!;
        JsonObject tasksProp = FindProp(props, DevSpace.Props.Tasks);
        var ids = ((JsonArray)tasksProp["objects"]!).Select(n => n!.GetValue<string>()).ToArray();

        Assert.Equal(new[] { TaskId, OtherTaskId }, ids);
    }

    // One write, and only an insert at the end: the entry is two paragraphs,
    // the local date and time, then the trimmed line. No If-Match, because an
    // append depends on nothing the read saw.
    [Fact]
    public async Task LogAppendsOneTwoParagraphEntryAtTheEndOfTheBody()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Report(withBlock: true))
            .Reply(HttpStatusCode.OK, "{}");

        await NewGestures(server, v2, new StoppedClock()).LogAsync("  writes the tests \n", NewReport, Ct);

        JsonArray ops = LoggedOps(v2);
        JsonObject op = (JsonObject)Assert.Single(ops)!;
        Assert.Equal("insert_blocks", op["op"]!.GetValue<string>());
        Assert.Equal("last", op["position"]!.GetValue<string>());
        Assert.Equal("2026-06-12 14:05\n\nwrites the tests\n\n ", op["markdown"]!.GetValue<string>());

        FakeV2Handler.Seen patch = v2.Requests.Single(r => r.Method == "PATCH");
        Assert.False(patch.HasHeader("If-Match"));
        Assert.False(string.IsNullOrWhiteSpace(patch.Header("Idempotency-Key")));
        // Nothing went through v1: the whole gesture is the read and the append.
        Assert.Empty(server.Requests);
    }

    // Anytype puts no space between paragraphs: without a divider, an entry's
    // text runs straight into the next entry's stamp. Between two entries
    // only — the first entry sits right under the heading.
    [Theory]
    [InlineData("paragraph")]
    [InlineData("bulleted")]
    public async Task LogSeparatesAnEntryFromThePreviousOneWithADivider(string earlierBlockType)
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Report(withBlock: true, "paragraph", earlierBlockType))
            .Reply(HttpStatusCode.OK, "{}");

        await NewGestures(server, v2, new StoppedClock()).LogAsync("writes the tests", NewReport, Ct);

        Assert.Equal(
            "---\n\n2026-06-12 14:05\n\nwrites the tests\n\n ",
            LoggedOps(v2).Single()!["markdown"]!.GetValue<string>());
    }

    // The caller just wrote the line: echoing it back only costs context.
    [Fact]
    public async Task LogAcknowledgesWithoutEchoingTheLineItAppended()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Report(withBlock: true))
            .Reply(HttpStatusCode.OK, "{}");

        string response = await NewGestures(server, v2, new StoppedClock())
            .LogAsync("a single line not to send back", NewReport, Ct);

        Assert.Equal("Noté.", response);
        Assert.DoesNotContain("single line", response);
        // The line itself did reach the report.
        Assert.Contains("a single line not to send back", LoggedOps(v2).Single()!["markdown"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "regression")]
    public async Task LogOnAFreshReportWritesTheJournalHeadingFirst(bool outlineKeyPresent)
    {
        using var server = new FakeAnytypeServer();
        // A report created on v1: Anytype consumed the creation body's
        // « # Journal … » heading as the note title, so its body has no block.
        // An entry written alone would become the note's first line — and
        // therefore its title. An empty outline and no outline at all both
        // mean « no block ».
        string read = outlineKeyPresent
            ? V2Report(withBlock: false)
            : "{\"properties\":{}}";
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, read)
            .Reply(HttpStatusCode.OK, "{}");

        await NewGestures(server, v2, new StoppedClock()).LogAsync("writes the tests", NewReport, Ct);

        // The heading is today's, in the same insert as the first entry.
        Assert.Equal(
            "# Journal 2026-06-12\n\n2026-06-12 14:05\n\nwrites the tests\n\n ",
            LoggedOps(v2).Single()!["markdown"]!.GetValue<string>());
    }

    // A report written alone, with no heading block, gets a dated heading:
    // today's, from the gesture's own clock, without a second read.
    [Fact]
    public async Task LogDatesTheHeadingTodayWhenTheReportHasNoHeadingBlock()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, "{\"properties\":{},\"outline\":[]}")
            .Reply(HttpStatusCode.OK, "{}");

        await NewGestures(server, v2, new StoppedClock()).LogAsync("writes the tests", NewReport, Ct);

        Assert.StartsWith(
            "# Journal 2026-06-12\n\n",
            LoggedOps(v2).Single()!["markdown"]!.GetValue<string>(),
            StringComparison.Ordinal);
        Assert.Empty(server.Requests);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task LogRequiresTheReportIdBeforeReadingOrWriting()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server, v2).LogAsync("writes the tests", "Journal du jour", Ct));

        Assert.Contains("id Anytype stable", error.Message);
        Assert.Empty(server.Requests);
        Assert.Empty(v2.Requests);
    }

    // The create POST (path .../objects).
    static FakeV2Handler.Seen CreateRequest(FakeV2Handler v2) =>
        Assert.Single(v2.Requests,
            r => r.Method == "POST" && r.PathAndQuery == "/v2/spaces/space-1/objects");

    static JsonObject CreateBody(FakeV2Handler v2) =>
        (JsonObject)JsonNode.Parse(CreateRequest(v2).Body!)!;

    static JsonObject FindProp(JsonArray props, string key)
    {
        foreach (JsonNode? node in props)
            if (node is JsonObject p && p["key"]?.GetValue<string>() == key)
                return p;
        throw new InvalidOperationException($"Property « {key} » not found in payload.");
    }
}
