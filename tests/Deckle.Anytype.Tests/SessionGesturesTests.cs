using System.Globalization;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for SessionGestures over the shared FakeAnytypeServer
// (defined in TaskGesturesTests.cs). These pin the anchoring contract under the
// inverted link model: a session report carries the journal date + the anchor
// task in « Tâche(s) liée(s) » (the link lives on the report side now, and the
// report has no project property); session_touch appends a further task to that
// property; a follow-up log is a read-modify-write of the report body.
[Trait("Category", "integration")]
public class SessionGesturesTests
{
    const string TaskId      = "bafyreiTaskbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string OtherTaskId = "bafyreiTask2bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string ProjectId   = "bafyreiProjccccccccccccccccccccccccccccccccccccccccccc";
    const string NewReport   = "bafyreiNewdddddddddddddddddddddddddddddddddddddddddddddd";

    static SessionGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new SessionGestures(client, new NameResolver(client));
    }

    static string Today() =>
        DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A task carrying one linked project (read by the digest). The properties
    // array is the raw API shape ({key, <format>: value}).
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

    // A report carrying the journal date and the given linked task ids.
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

    // Registers the routes session_start touches: GET task, POST create (returns
    // the new report id), GET project (digest), and an empty report search — the
    // anchor task has no past reports, so the digest skips that block.
    static void WireStartRoutes(FakeAnytypeServer server)
    {
        server.OnGetObject(TaskId, TaskObject(TaskId, "Tâche d'ancrage"));
        server.OnPostObject(new JsonObject { ["object"] = new JsonObject { ["id"] = NewReport } });
        server.OnGetObject(ProjectId, ProjectObject());
        server.OnSearch(new JsonObject { ["data"] = new JsonArray() });
    }

    [Fact]
    public async Task StartCreatesAReportCarryingTodayAndTheAnchorTask()
    {
        using var server = new FakeAnytypeServer();
        WireStartRoutes(server);

        await NewGestures(server).StartAsync(TaskId);

        // The created rapport's payload carries date_du_journal = today and the
        // anchor task in « Tâche(s) liée(s) ».
        JsonObject create = CreateBody(server);
        Assert.Equal(DevSpace.Types.Rapport, create["type_key"]!.GetValue<string>());

        var props = (JsonArray)create["properties"]!;
        JsonObject dateProp = FindProp(props, DevSpace.Props.DateDuJournal);
        Assert.Equal(Today(), dateProp["date"]!.GetValue<string>());
        // Independent of the Today() mirror: the wire date stays ISO yyyy-MM-dd.
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", dateProp["date"]!.GetValue<string>());

        JsonObject taskProp = FindProp(props, DevSpace.Props.TachesLiees);
        Assert.Equal(TaskId, ((JsonArray)taskProp["objects"]!).Single()!.GetValue<string>());

        // The link lives on the report side now: the report has NO project property
        // (its project is derived through the linked task(s)).
        Assert.DoesNotContain(props,
            n => n is JsonObject p && p["key"]?.GetValue<string>() == DevSpace.Props.RelationProjet);
    }

    [Fact]
    public async Task TouchAppendsTheTaskToTheCurrentReportsLinkedTasks()
    {
        using var server = new FakeAnytypeServer();
        WireStartRoutes(server);
        // session_touch reads the current report, then PATCHes it. The report was
        // born linking the anchor task; touching another task unions it in.
        server.OnGetObject(OtherTaskId, TaskObject(OtherTaskId, "Autre tâche"));
        server.OnGetObject(NewReport, ReportObject(NewReport, "# Journal", TaskId));
        server.OnPatchObject(NewReport, ReportObject(NewReport, "# Journal", TaskId, OtherTaskId));

        var gestures = NewGestures(server);
        await gestures.StartAsync(TaskId);
        await gestures.TouchTaskAsync(OtherTaskId);

        // The report PATCH writes « Tâche(s) liée(s) » = anchor task + the touched
        // task, in order — the pre-existing link is preserved.
        JsonObject patch = server.LastBodyFor("PATCH");
        var props = (JsonArray)patch["properties"]!;
        JsonObject tasksProp = FindProp(props, DevSpace.Props.TachesLiees);
        var ids = ((JsonArray)tasksProp["objects"]!).Select(n => n!.GetValue<string>()).ToArray();

        Assert.Equal(new[] { TaskId, OtherTaskId }, ids);
    }

    [Fact]
    public async Task LogAfterStartAppendsOneLineToTheReportBody()
    {
        using var server = new FakeAnytypeServer();
        WireStartRoutes(server);
        // The log reads then rewrites the NEW report (the one Start opened).
        string existingBody = "# Journal " + Today();
        server.OnGetObject(NewReport, ReportObject(NewReport, existingBody, TaskId));
        server.OnPatchObject(NewReport, ReportObject(NewReport, existingBody, TaskId));

        var gestures = NewGestures(server);
        await gestures.StartAsync(TaskId);
        await gestures.LogAsync("écrit les tests");

        // Read-modify-write: the report PATCH body is the previous body plus the
        // new "- line" entry appended.
        JsonObject patch = server.LastBodyFor("PATCH");
        Assert.Equal(existingBody + "\n- écrit les tests", patch["markdown"]!.GetValue<string>());
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
