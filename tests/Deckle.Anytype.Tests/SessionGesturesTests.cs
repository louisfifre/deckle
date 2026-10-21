using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for SessionGestures over the shared FakeAnytypeServer
// (defined in TaskGesturesTests.cs). A session report carries the journal date
// and the anchor task in « Tâche(s) liée(s) » (the report has no project
// property); session_touch appends a further task to an explicit report; a
// follow-up log appends blocks to the report body through the v2 client (over
// FakeV2Handler), the rest of the gestures still talk v1.
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

    // What a v2 object read answers for the log's read: the journal date as a
    // property and, unless the body is empty, an outline with one block.
    static string V2Report(bool withBlock)
    {
        string outline = withBlock ? "[{\"id\":\"b1\"}]" : "[]";
        return "{\"properties\":{\"" + DevSpace.Props.DateDuJournal
            + "\":\"2026-06-11T00:00:00Z\"},\"outline\":" + outline + "}";
    }

    // The single PATCH the log sends, as its parsed ops array.
    static JsonArray LoggedOps(FakeV2Handler handler)
    {
        FakeV2Handler.Seen patch = Assert.Single(handler.Requests, r => r.Method == "PATCH");
        Assert.Equal(LogPath, patch.PathAndQuery);
        return (JsonArray)JsonNode.Parse(patch.Body!)!["ops"]!;
    }

    static string Today() =>
        DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static JsonObject TaskObject(string id, string name) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["markdown"] = "",
            ["properties"] = new JsonArray(new JsonObject
            {
                ["key"] = DevSpace.Props.RelationProjet,
                ["objects"] = new JsonArray(ProjectId),
            }),
        },
    };

    static JsonObject ProjectObject() => new()
    {
        ["object"] = new JsonObject { ["id"] = ProjectId, ["name"] = "Mon projet" },
    };

    static JsonObject ReportObject(string id, string markdown, params string[] taskIds) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = id,
            ["markdown"] = markdown,
            ["properties"] = new JsonArray(
                new JsonObject { ["key"] = DevSpace.Props.DateDuJournal, ["date"] = "2026-06-11" },
                new JsonObject { ["key"] = DevSpace.Props.TachesLiees, ["objects"] = Refs(taskIds) }),
        },
    };

    static JsonArray Refs(string[] ids)
    {
        var array = new JsonArray();
        foreach (string id in ids) array.Add(id);
        return array;
    }

    // By default the anchor task has no past reports, so the report search
    // answers empty and the digest skips that block. A test that wants past
    // reports passes the search hits (and registers each report's own GET).
    static void WireStartRoutes(FakeAnytypeServer server, JsonArray? reportHits = null)
    {
        server.OnGetObject(TaskId, TaskObject(TaskId, "Tâche d'ancrage"));
        server.OnPostObject(new JsonObject { ["object"] = new JsonObject { ["id"] = NewReport } });
        server.OnGetObject(ProjectId, ProjectObject());
        server.OnSearch(new JsonObject { ["data"] = reportHits ?? new JsonArray() });
    }

    // A past report: its body, its journal date (the digest sorts on it) and the
    // one task it is linked to.
    static JsonObject DatedReport(string id, string date, string markdown, string taskId) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = id,
            ["markdown"] = markdown,
            ["properties"] = new JsonArray(
                new JsonObject { ["key"] = DevSpace.Props.DateDuJournal, ["date"] = date },
                new JsonObject { ["key"] = DevSpace.Props.TachesLiees, ["objects"] = Refs([taskId]) }),
        },
    };

    // A search hit carries the report without the "object" wrapper.
    static JsonObject AsHit(JsonObject report) => (JsonObject)report["object"]!.DeepClone();

    [Fact]
    public async Task StartCreatesAReportCarryingTodayAndTheAnchorTask()
    {
        using var server = new FakeAnytypeServer();
        WireStartRoutes(server);

        string digest = await NewGestures(server).StartAsync(TaskId, Ct);

        Assert.StartsWith($"report_id : {NewReport}\n", digest, StringComparison.Ordinal);

        JsonObject create = CreateBody(server);
        Assert.Equal(DevSpace.Types.Rapport, create["type_key"]!.GetValue<string>());

        var props = (JsonArray)create["properties"]!;
        JsonObject dateProp = FindProp(props, DevSpace.Props.DateDuJournal);
        Assert.Equal(Today(), dateProp["date"]!.GetValue<string>());
        // Independent of the Today() mirror: the wire date stays ISO yyyy-MM-dd.
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", dateProp["date"]!.GetValue<string>());

        JsonObject taskProp = FindProp(props, DevSpace.Props.TachesLiees);
        Assert.Equal(TaskId, ((JsonArray)taskProp["objects"]!).Single()!.GetValue<string>());

        // The report has NO project property: its project is derived through the
        // linked task(s).
        Assert.DoesNotContain(props,
            n => n is JsonObject p && p["key"]?.GetValue<string>() == DevSpace.Props.RelationProjet);
    }

    // The digest places the session, it does not replay it: only the most recent
    // reports of the task appear, newest first, each as its date and id (the
    // handle report_journal takes) followed by the tail of its journal. A report
    // longer than the tail says how many lines it leaves out.
    [Fact]
    public async Task StartShowsOnlyTheRecentReportsOfTheTaskEachWithItsTail()
    {
        using var server = new FakeAnytypeServer();

        // One report more than the digest shows; the oldest must drop out. Hits
        // go in oldest first, so the newest-first order comes from the sort.
        int reportCount = SessionGestures.RecentReports + 1;
        string IdOf(int i) => $"bafyreiReport{i}" + new string('e', 40);
        string DateOf(int i) => $"2026-06-{i + 1:00}";

        var hits = new JsonArray();
        for (int i = 0; i < reportCount; i++)
        {
            bool newest = i == reportCount - 1;
            string[] entries = newest
                ? Enumerable.Range(1, SessionGestures.ReportTailEntries + 2).Select(n => $"- ligne {n}").ToArray()
                : [$"- a{i}", $"- b{i}"];
            JsonObject report = DatedReport(IdOf(i), DateOf(i), $"# Journal {DateOf(i)}\n" + string.Join("\n", entries), TaskId);
            server.OnGetObject(IdOf(i), report);
            hits.Add(AsHit(report));
        }

        // A later report of ANOTHER task is never part of this task's digest.
        const string StrangerId = "bafyreiStrangerffffffffffffffffffffffffffffffffffffff";
        hits.Add(AsHit(DatedReport(StrangerId, "2026-07-01", "# Journal 2026-07-01\n- étranger", OtherTaskId)));
        WireStartRoutes(server, hits);

        string digest = await NewGestures(server).StartAsync(TaskId, Ct);

        var expected = new List<string>
        {
            $"report_id : {NewReport}",
            "[ ] Tâche d'ancrage · projet Mon projet",
        };
        for (int i = reportCount - 1; i >= reportCount - SessionGestures.RecentReports; i--)
        {
            expected.Add($"— {DateOf(i)} · {IdOf(i)}");
            if (i == reportCount - 1)
            {
                // ReportTailEntries + 2 entries: the first two are left out.
                expected.Add("… 2 entrée(s) avant");
                expected.AddRange(Enumerable.Range(3, SessionGestures.ReportTailEntries).Select(n => $"- ligne {n}"));
            }
            else
            {
                expected.AddRange([$"- a{i}", $"- b{i}"]);
            }
        }

        Assert.Equal(string.Join("\n", expected), digest);
        Assert.DoesNotContain(IdOf(0), digest);
        Assert.DoesNotContain(StrangerId, digest);
    }

    [Fact]
    public async Task TouchAppendsTheTaskToTheCurrentReportsLinkedTasks()
    {
        using var server = new FakeAnytypeServer();
        WireStartRoutes(server);
        server.OnGetObject(OtherTaskId, TaskObject(OtherTaskId, "Autre tâche"));
        server.OnGetObject(NewReport, ReportObject(NewReport, "# Journal", TaskId));
        server.OnPatchObject(NewReport, ReportObject(NewReport, "# Journal", TaskId, OtherTaskId));

        var gestures = NewGestures(server);
        await gestures.TouchTaskAsync(NewReport, OtherTaskId, Ct);

        JsonObject patch = server.LastBodyFor("PATCH");
        var props = (JsonArray)patch["properties"]!;
        JsonObject tasksProp = FindProp(props, DevSpace.Props.TachesLiees);
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
        Assert.Equal("2026-06-12 14:05\n\nwrites the tests", op["markdown"]!.GetValue<string>());

        FakeV2Handler.Seen patch = v2.Requests.Single(r => r.Method == "PATCH");
        Assert.False(patch.HasHeader("If-Match"));
        Assert.False(string.IsNullOrWhiteSpace(patch.Header("Idempotency-Key")));
        // Nothing went through v1: the whole gesture is the read and the append.
        Assert.Empty(server.Requests);
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
        // A report fresh out of session_start: Anytype consumed the creation
        // body's « # Journal … » heading as the note title, so its body has no
        // block. An entry written alone would become the note's first line —
        // and therefore its title. An empty outline and no outline at all both
        // mean « no block ».
        string read = outlineKeyPresent
            ? V2Report(withBlock: false)
            : "{\"properties\":{\"" + DevSpace.Props.DateDuJournal + "\":\"2026-06-11\"}}";
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, read)
            .Reply(HttpStatusCode.OK, "{}");

        await NewGestures(server, v2, new StoppedClock()).LogAsync("writes the tests", NewReport, Ct);

        // The heading comes from the report's own journal date (2026-06-11),
        // not from today, in the same insert as the first entry.
        Assert.Equal(
            "# Journal 2026-06-11\n\n2026-06-12 14:05\n\nwrites the tests",
            LoggedOps(v2).Single()!["markdown"]!.GetValue<string>());
    }

    // The v2 read shape of a date is not measured: when the property is not
    // a string, the journal date is read the way v1 does.
    [Fact]
    public async Task LogReadsTheJournalDateThroughV1WhenV2GivesNone()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(NewReport, ReportObject(NewReport, "", TaskId));
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, "{\"properties\":{},\"outline\":[]}")
            .Reply(HttpStatusCode.OK, "{}");

        await NewGestures(server, v2, new StoppedClock()).LogAsync("writes the tests", NewReport, Ct);

        Assert.StartsWith(
            "# Journal 2026-06-11\n\n",
            LoggedOps(v2).Single()!["markdown"]!.GetValue<string>(),
            StringComparison.Ordinal);
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

    // The create POST body (path .../objects) — session_start also POSTs a /search
    // for the digest, so LastBodyFor("POST") would return that search body instead.
    static JsonObject CreateBody(FakeAnytypeServer server)
    {
        var req = server.Requests.Last(
            r => r.Method == "POST" && r.Path.EndsWith("/objects", StringComparison.Ordinal));
        return (JsonObject)JsonNode.Parse(req.Body)!;
    }

    static JsonObject FindProp(JsonArray props, string key)
    {
        foreach (JsonNode? node in props)
            if (node is JsonObject p && p["key"]?.GetValue<string>() == key)
                return p;
        throw new InvalidOperationException($"Property « {key} » not found in payload.");
    }
}
