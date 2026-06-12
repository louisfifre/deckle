using System.Globalization;
using System.Text.Json.Nodes;
using Deckle.Anytype.Api;
using Deckle.Anytype.Gestures;
using Deckle.Anytype.Schema;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for SessionGestures over the shared FakeAnytypeServer
// (defined in TaskGesturesTests.cs). These pin the anchoring contract: a session
// report carries the journal date + the anchor task's project, and the anchor
// task gets the report id APPENDED to « Rapport(s) lié(s) » (pre-existing ids
// preserved). Then a follow-up log is a read-modify-write of the report body.
[Trait("Category", "integration")]
public class SessionGesturesTests
{
    const string TaskId    = "bafyreiTaskbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string ProjectId = "bafyreiProjccccccccccccccccccccccccccccccccccccccccccc";
    const string OldReport = "bafyreiOldddddddddddddddddddddddddddddddddddddddddddddd";
    const string NewReport = "bafyreiNewdddddddddddddddddddddddddddddddddddddddddddddd";

    static SessionGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new SessionGestures(client, new NameResolver(client));
    }

    static string Today() =>
        DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The anchor task: one linked project, one pre-existing linked report. The
    // properties array is the raw API shape ({key, <format>: value}).
    static JsonObject TaskObject() => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = TaskId,
            ["name"] = "Tâche d'ancrage",
            ["markdown"] = "",
            ["properties"] = new JsonArray(
                new JsonObject
                {
                    ["key"] = DevSpace.Props.RelationProjet,
                    ["objects"] = new JsonArray(ProjectId),
                },
                new JsonObject
                {
                    ["key"] = DevSpace.Props.RapportsLies,
                    ["objects"] = new JsonArray(OldReport),
                }),
        },
    };

    static JsonObject ProjectObject() => new()
    {
        ["object"] = new JsonObject { ["id"] = ProjectId, ["name"] = "Mon projet" },
    };

    static JsonObject ReportObject(string id, string markdown) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = id,
            ["markdown"] = markdown,
            ["properties"] = new JsonArray(new JsonObject
            {
                ["key"] = DevSpace.Props.DateDuJournal,
                ["date"] = "2026-06-11",
            }),
        },
    };

    // Registers every route StartAsync touches: GET task, POST create (returns the
    // new report id), PATCH task (anchor), and the digest reads (project + the
    // pre-existing report).
    static void WireStartRoutes(FakeAnytypeServer server)
    {
        server.OnGetObject(TaskId, TaskObject());
        server.OnPostObject(new JsonObject { ["object"] = new JsonObject { ["id"] = NewReport } });
        server.OnPatchObject(TaskId, TaskObject());
        server.OnGetObject(ProjectId, ProjectObject());
        server.OnGetObject(OldReport, ReportObject(OldReport, "# Journal 2026-06-11\n- note"));
    }

    [Fact]
    public async Task StartCreatesAReportCarryingTodayAndTheTasksProject()
    {
        using var server = new FakeAnytypeServer();
        WireStartRoutes(server);

        await NewGestures(server).StartAsync(TaskId);

        // The created rapport's payload carries date_du_journal = today and
        // relation_projet copied from the anchor task.
        JsonObject create = server.LastBodyFor("POST");
        Assert.Equal(DevSpace.Types.Rapport, create["type_key"]!.GetValue<string>());

        var props = (JsonArray)create["properties"]!;
        JsonObject dateProp = FindProp(props, DevSpace.Props.DateDuJournal);
        Assert.Equal(Today(), dateProp["date"]!.GetValue<string>());

        JsonObject projProp = FindProp(props, DevSpace.Props.RelationProjet);
        Assert.Equal(ProjectId, ((JsonArray)projProp["objects"]!).Single()!.GetValue<string>());
    }

    [Fact]
    public async Task StartAppendsTheReportIdToExistingRapportsLiesOnTheTask()
    {
        using var server = new FakeAnytypeServer();
        WireStartRoutes(server);

        await NewGestures(server).StartAsync(TaskId);

        // The anchor PATCH writes « Rapport(s) lié(s) » = pre-existing ids + the
        // new report id, in order — the pre-existing link is preserved.
        JsonObject patch = server.LastBodyFor("PATCH");
        var props = (JsonArray)patch["properties"]!;
        JsonObject rapportsProp = FindProp(props, DevSpace.Props.RapportsLies);
        var ids = ((JsonArray)rapportsProp["objects"]!)
            .Select(n => n!.GetValue<string>()).ToArray();

        Assert.Equal(new[] { OldReport, NewReport }, ids);
    }

    [Fact]
    public async Task LogAfterStartAppendsOneLineToTheReportBody()
    {
        using var server = new FakeAnytypeServer();
        WireStartRoutes(server);
        // The log reads then rewrites the NEW report (the one Start opened).
        string existingBody = "# Journal " + Today();
        server.OnGetObject(NewReport, ReportObject(NewReport, existingBody));
        server.OnPatchObject(NewReport, ReportObject(NewReport, existingBody));

        var gestures = NewGestures(server);
        await gestures.StartAsync(TaskId);
        await gestures.LogAsync("écrit les tests");

        // Read-modify-write: the report PATCH body is the previous body plus the
        // new "- line" entry appended.
        JsonObject patch = server.LastBodyFor("PATCH");
        Assert.Equal(existingBody + "\n- écrit les tests", patch["markdown"]!.GetValue<string>());
    }

    static JsonObject FindProp(JsonArray props, string key)
    {
        foreach (JsonNode? node in props)
            if (node is JsonObject p && p["key"]?.GetValue<string>() == key)
                return p;
        throw new InvalidOperationException($"Property « {key} » not found in payload.");
    }
}
