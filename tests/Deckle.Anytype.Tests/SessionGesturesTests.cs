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
// follow-up log appends blocks to the report body. session_start and
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

    // What a v2 object read answers for the log's read: the journal date as a
    // property and, unless the body is empty, an outline holding the « # Journal »
    // heading block a fresh v2 report opens on, then one block per type given.
    static string V2Report(bool withBlock, params string[] moreBlockTypes)
    {
        var blocks = new JsonArray();
        if (withBlock) blocks.Add(new JsonObject { ["id"] = "h1", ["type"] = "heading_1" });
        for (int i = 0; i < moreBlockTypes.Length; i++)
            blocks.Add(new JsonObject { ["id"] = $"b{i}", ["type"] = moreBlockTypes[i] });
        return "{\"properties\":{\"" + DevSpace.Props.DateDuJournal
            + "\":\"2026-06-11T00:00:00Z\"},\"outline\":" + blocks.ToJsonString() + "}";
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

    // The v2 answers session_start's requests, in the order they are sent: the
    // task's properties, the task's markdown, the report create, one properties
    // read per linked project, the report search, then one markdown read per
    // shown report.
    static string V2Named(string id, string type, string name, string extraProperties = "") =>
        "{\"id\":\"" + id + "\",\"type\":\"" + type + "\",\"properties\":{\"name\":\"" + name + "\""
        + extraProperties + "}}";

    static string V2Markdown(string id, string markdown) =>
        new JsonObject { ["id"] = id, ["type"] = "task", ["markdown"] = markdown }.ToJsonString();

    static string V2Rows(params (string Id, string Date)[] reports) => new JsonObject
    {
        ["data"] = new JsonArray(reports.Select(r => (JsonNode)new JsonObject
        {
            ["id"] = r.Id,
            ["name"] = "",
            ["type"] = "rapport",
            ["properties"] = new JsonObject { [DevSpace.Props.DateDuJournal] = r.Date + "T00:00:00Z" },
        }).ToArray()),
        ["total"] = reports.Length,
        ["has_more"] = false,
    }.ToJsonString();

    // By default the anchor task has no past reports, so the report search
    // answers empty and the digest skips that block.
    static FakeV2Handler WireStartReads(string searchAnswer, string? createAnswer = null) =>
        new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Named(TaskId, "task", "Tâche d'ancrage",
                ",\"" + DevSpace.Props.RelationProjet + "\":[\"" + ProjectId + "\"]"))
            .Reply(HttpStatusCode.OK, V2Markdown(TaskId, "# Tâche d'ancrage\n- [x] fait\n- [ ] à faire"))
            // The shape of a real create's answer (measured): id and etag at the root.
            .Reply(HttpStatusCode.Created,
                createAnswer ?? "{\"id\":\"" + NewReport + "\",\"type\":\"rapport\",\"etag\":\"1ea04e49\"}")
            .Reply(HttpStatusCode.OK, V2Named(ProjectId, "project", "Mon projet"))
            .Reply(HttpStatusCode.OK, searchAnswer);

    [Fact]
    public async Task StartCreatesAReportCarryingTodayAndTheAnchorTask()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = WireStartReads(V2Rows());

        string digest = await NewGestures(server, v2).StartAsync(TaskId, Ct);

        Assert.StartsWith($"report_id : {NewReport}\n", digest, StringComparison.Ordinal);

        JsonObject create = CreateBody(v2);
        Assert.Equal(DevSpace.Types.Rapport, create["type"]!.GetValue<string>());
        // The heading opens the body; a report is a note and is sent no name,
        // which would land as a paragraph ahead of it.
        Assert.Equal($"# Journal {Today()}", create["markdown"]!.GetValue<string>());
        Assert.False(create.ContainsKey("name"));

        var props = (JsonObject)create["properties"]!;
        string date = props[DevSpace.Props.DateDuJournal]!.GetValue<string>();
        Assert.Equal(Today(), date);
        // Independent of the Today() mirror: the wire date stays ISO yyyy-MM-dd.
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", date);

        Assert.Equal(TaskId, ((JsonArray)props[DevSpace.Props.TachesLiees]!).Single()!.GetValue<string>());

        // The report has NO project property: its project is derived through the
        // linked task(s).
        Assert.False(props.ContainsKey(DevSpace.Props.RelationProjet));

        // A resend after a 5xx must not open a second report.
        FakeV2Handler.Seen post = CreateRequest(v2);
        Assert.False(string.IsNullOrWhiteSpace(post.Header("Idempotency-Key")));
    }

    // Every later session_log is addressed by this id: a create answer without
    // one is a failure, not an empty report_id handed to the caller.
    [Fact]
    public async Task StartFailsWhenTheCreateAnswerCarriesNoId()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = WireStartReads(V2Rows(), createAnswer: "{\"type\":\"rapport\"}");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, v2).StartAsync(TaskId, Ct));
    }

    [Fact]
    public async Task StartDigestHeadsWithTheTaskItsProjectAndItsChecklist()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = WireStartReads(V2Rows());

        string digest = await NewGestures(server, v2).StartAsync(TaskId, Ct);

        Assert.Equal(
            $"report_id : {NewReport}\n[ ] Tâche d'ancrage · projet Mon projet\n- [x] fait\n- [ ] à faire",
            digest);
        // Nothing of session_start goes through v1 any more.
        Assert.Empty(server.Requests);
    }

    // The digest places the session, it does not replay it: only the most recent
    // reports of the task appear, newest first, each as its date and id (the
    // handle report_journal takes) followed by the tail of its journal. A report
    // longer than the tail says how many lines it leaves out.
    [Fact]
    public async Task StartShowsOnlyTheRecentReportsOfTheTaskEachWithItsTail()
    {
        using var server = new FakeAnytypeServer();

        // One report more than the digest shows; the oldest must drop out. The
        // server sorts, so the rows come newest first.
        int reportCount = SessionGestures.RecentReports + 1;
        string IdOf(int i) => $"bafyreiReport{i}" + new string('e', 40);
        string DateOf(int i) => $"2026-06-{i + 1:00}";

        var rows = new List<(string, string)>();
        var markdowns = new List<string>();
        for (int i = reportCount - 1; i >= 0; i--)
        {
            rows.Add((IdOf(i), DateOf(i)));
            bool newest = i == reportCount - 1;
            string[] entries = newest
                ? Enumerable.Range(1, SessionGestures.ReportTailEntries + 2).Select(n => $"- ligne {n}").ToArray()
                : [$"- a{i}", $"- b{i}"];
            markdowns.Add($"# Journal {DateOf(i)}\n" + string.Join("\n", entries));
        }

        FakeV2Handler v2 = WireStartReads(V2Rows(rows.ToArray()));
        // Only the shown reports are read for their body.
        for (int k = 0; k < SessionGestures.RecentReports; k++)
            v2.Reply(HttpStatusCode.OK, V2Markdown(rows[k].Item1, markdowns[k]));

        string digest = await NewGestures(server, v2).StartAsync(TaskId, Ct);

        var expected = new List<string>
        {
            $"report_id : {NewReport}",
            "[ ] Tâche d'ancrage · projet Mon projet",
            "- [x] fait",
            "- [ ] à faire",
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

        // The server does the filtering and the ordering: the reports of the
        // task only, newest first.
        FakeV2Handler.Seen search = Assert.Single(v2.Requests, r => r.PathAndQuery.Contains("/search?"));
        // One page, not every report of the task: the shown ones plus the slot
        // the report just created may take.
        Assert.EndsWith($"/search?limit={SessionGestures.RecentReports + 1}&offset=0", search.PathAndQuery);
        JsonObject body = (JsonObject)JsonNode.Parse(search.Body!)!;
        Assert.Equal(DevSpace.Types.Rapport, body["type"]!.GetValue<string>());
        JsonObject filter = (JsonObject)Assert.Single((JsonArray)body["filters"]!)!;
        Assert.Equal(DevSpace.Props.TachesLiees, filter["property"]!.GetValue<string>());
        Assert.Equal(TaskId,((JsonArray)filter["value"]!).Single()!.GetValue<string>());
        Assert.Equal("desc", ((JsonObject)Assert.Single((JsonArray)body["sorts"]!)!)["direction"]!.GetValue<string>());
    }
    // The report session_start has just created links the task too, so the
    // search returns it first. It is the session being opened, not history: the
    // three slots go to the previous reports.
    [Fact]
    [Trait("Category", "regression")]
    public async Task StartLeavesTheReportItJustCreatedOutOfTheDigest()
    {
        using var server = new FakeAnytypeServer();
        string[] previous = ["bafyreiPrevaaa", "bafyreiPrevbbb", "bafyreiPrevccc"];
        FakeV2Handler v2 = WireStartReads(V2Rows(
            (NewReport, "2026-06-12"),
            (previous[0], "2026-06-11"),
            (previous[1], "2026-06-10"),
            (previous[2], "2026-06-09")));
        // Only the previous reports are read for their body.
        foreach (string id in previous)
            v2.Reply(HttpStatusCode.OK, V2Markdown(id, "- fait"));

        string digest = await NewGestures(server, v2).StartAsync(TaskId, Ct);

        Assert.DoesNotContain($"— 2026-06-12 · {NewReport}", digest);
        foreach (string id in previous) Assert.Contains(id, digest);
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
            "---\n\n2026-06-12 14:05\n\nwrites the tests",
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

    // A report that lost its journal date still gets a dated heading: today's,
    // from the gesture's own clock, without a second read.
    [Fact]
    public async Task LogDatesTheHeadingTodayWhenTheReportHasNoJournalDate()
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

    // The create POST (path .../objects) — session_start also POSTs a /search
    // for the digest.
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
