using System.Net;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for ProjectGestures over the shared FakeAnytypeServer.
// Selectors are bafy* ids so the resolver short-circuits and no /search route
// is needed (same convention as TaskGesturesTests).
[Trait("Category", "integration")]
public class ProjectGesturesTests
{
    const string ProjectId = "bafyreiprojectaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string EpicId    = "bafyreiepicaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string ActiveTaskId = "bafyreiTaskactiveaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string ArchivedTaskId = "bafyreiTaskarchivedaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string OldTaskId = "bafyreiTaskoldaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string OtherProjectId = "bafyreiprojectotheraaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static ProjectGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new ProjectGestures(client, new NameResolver(client), client.V2);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task CreateEpicPassesTheEpicTemplateIdSoTheEpicIsBornWithItsViews()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostObject(new JsonObject
        {
            ["object"] = new JsonObject { ["id"] = EpicId, ["name"] = "Deckle" },
        });

        await NewGestures(server).CreateEpicAsync("Deckle", state: "en_cours", ct: Ct);

        JsonObject created = server.LastBodyFor("POST");
        Assert.Equal(DevSpace.Types.Epic, created["type_key"]!.GetValue<string>());
        Assert.Equal(DevSpace.Templates.Epic, created["template_id"]!.GetValue<string>());
        JsonObject state = Assert.IsType<JsonObject>(
            Assert.Single((JsonArray)created["properties"]!));
        Assert.Equal(DevSpace.Props.Etat, state["key"]!.GetValue<string>());
        Assert.Equal("en_cours", state["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreatePassesTheProjectTemplateIdSoTheProjectIsBornFromItsTemplate()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostObject(new JsonObject
        {
            ["object"] = new JsonObject { ["id"] = ProjectId, ["name"] = "Mon projet" },
        });

        // No epic, so the only POST is the object creation — LastBodyFor("POST")
        // is the creation body (with an epic, the trailing POST is the list-add).
        await NewGestures(server).CreateAsync("Mon projet", ct: Ct);

        JsonObject created = server.LastBodyFor("POST");
        Assert.Equal(DevSpace.Templates.Project, created["template_id"]!.GetValue<string>());
    }

    // The live list-add endpoint answers 200 with a bare JSON string, not an
    // object. Creating a project inside an epic must treat that as success:
    // the membership is already posted.
    [Fact]
    public async Task CreateInsideAnEpicSurvivesTheBareStringListAddResponse()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostObject(new JsonObject
        {
            ["object"] = new JsonObject { ["id"] = ProjectId, ["name"] = "Mon projet" },
        });
        server.OnPostListObjects(EpicId, "\"Objects added successfully\"");
        server.OnGetObject(EpicId, new JsonObject
        {
            ["object"] = new JsonObject { ["id"] = EpicId, ["name"] = "Deckle" },
        });

        string digest = await NewGestures(server).CreateAsync("Mon projet", epic: EpicId, ct: Ct);

        Assert.Contains("Ajouté à l'epic Deckle", digest);

        JsonObject added = server.LastBodyFor("POST");
        var objects = Assert.IsType<JsonArray>(added["objects"]);
        string id = Assert.Single(objects)!.GetValue<string>();
        Assert.Equal(ProjectId, id);
    }
    // ── Reads over the v2 client ─────────────────────────────────────────────

    const string ReportId = "bafyreiReportaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    // The resolver is never reached (selectors are ids) and creation is not
    // exercised here, so the v1 client is a placeholder; only the v2 handler talks.
    static ProjectGestures NewReader(FakeV2Handler handler)
    {
        var credentials = new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1");
        var v1 = new AnytypeApiClient(credentials);
        var v2 = new AnytypeV2Client(credentials, null, handler);
        return new ProjectGestures(v1, new NameResolver(v1), v2);
    }

    static string ProjectRead(bool done = false) => new JsonObject
    {
        ["id"] = ProjectId,
        ["name"] = "Refonte Anytype",
        ["type"] = "project",
        ["properties"] = new JsonObject
        {
            ["name"] = "Refonte Anytype",
            ["etat"] = new JsonArray("Ouvert"),
            ["priorite"] = new JsonArray("4"),
            ["done"] = done ? true : null,
        },
    }.ToJsonString();

    static string SearchPage(bool hasMore, params JsonObject[] rows) => new JsonObject
    {
        ["data"] = new JsonArray(rows),
        ["total"] = rows.Length,
        ["has_more"] = hasMore,
    }.ToJsonString();

    static JsonObject Row(string id, string name, string type, JsonObject properties) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["type"] = type,
        ["properties"] = properties,
    };

    static JsonObject TaskRow(string id, string name, bool archived = false) => Row(id, name, "task", new JsonObject
    {
        ["archive"] = archived ? true : null,
        ["priorite"] = new JsonArray("2"),
    });

    static JsonObject ReportRow() => Row(ReportId, "", "rapport", new JsonObject
    {
        ["date_du_journal"] = "2026-07-27T00:00:00Z",
    });

    static string Markdown(string body) =>
        new JsonObject { ["id"] = ReportId, ["type"] = "rapport", ["markdown"] = body }.ToJsonString();

    static JsonObject SearchBody(FakeV2Handler.Seen seen) => (JsonObject)JsonNode.Parse(seen.Body!)!;

    [Fact]
    public async Task OverviewAsksTheServerForTheProjectsTasksThenTheirReportsNewestFirst()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, ProjectRead())
            .Reply(HttpStatusCode.OK, SearchPage(false, TaskRow(ActiveTaskId, "Tâche active")))
            .Reply(HttpStatusCode.OK, SearchPage(false, ReportRow()))
            // Opens on blank lines, a divider and the heading: the line shown is the
            // latest entry, cleaned.
            .Reply(HttpStatusCode.OK, Markdown(
                "\n---\n# Journal 2026-07-27   \n2026-07-27 10:00\n\nancienne\n\n2026-07-27 11:30   \n\nmodule\\_X fini   \n\nsuite"));

        string digest = await NewReader(handler).OverviewAsync(ProjectId, Ct);

        Assert.Equal(
            $"id : {ProjectId}\n[ ] Refonte Anytype\nÉtat : Ouvert\nPriorité : 4\n"
            + $"Tâches :\n[ ] Tâche active · P2\n"
            + $"Derniers rapports :\n2026-07-27 · {ReportId} — 2026-07-27 11:30 · module_X fini",
            digest);

        FakeV2Handler.Seen tasks = handler.Requests[1];
        Assert.EndsWith("/search?limit=100&offset=0", tasks.PathAndQuery);
        JsonObject taskFilter = (JsonObject)Assert.Single((JsonArray)SearchBody(tasks)["filters"]!)!;
        Assert.Equal(DevSpace.Props.RelationProjet, taskFilter["property"]!.GetValue<string>());
        Assert.Equal(ProjectId, ((JsonArray)taskFilter["value"]!).Single()!.GetValue<string>());

        // One page of the size shown, not every report of the project.
        Assert.EndsWith("/search?limit=3&offset=0", handler.Requests[2].PathAndQuery);
        JsonObject reports = SearchBody(handler.Requests[2]);
        Assert.Equal(DevSpace.Types.Rapport, reports["type"]!.GetValue<string>());
        JsonObject reportFilter = (JsonObject)Assert.Single((JsonArray)reports["filters"]!)!;
        Assert.Equal(DevSpace.Props.TachesLiees, reportFilter["property"]!.GetValue<string>());
        Assert.Equal(ActiveTaskId, ((JsonArray)reportFilter["value"]!).Single()!.GetValue<string>());
        JsonObject sort = (JsonObject)Assert.Single((JsonArray)reports["sorts"]!)!;
        Assert.Equal("desc", sort["direction"]!.GetValue<string>());
    }

    [Fact]
    public async Task OverviewOmitsArchivedTasksButKeepsTheirReports()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, ProjectRead())
            .Reply(HttpStatusCode.OK, SearchPage(false,
                TaskRow(ActiveTaskId, "Tâche active"),
                TaskRow(ArchivedTaskId, "Tâche archivée", archived: true)))
            .Reply(HttpStatusCode.OK, SearchPage(false, ReportRow()))
            .Reply(HttpStatusCode.OK, Markdown("Rapport de la tâche archivée"));

        string digest = await NewReader(handler).OverviewAsync(ProjectId, Ct);

        Assert.Contains("Tâche active", digest);
        Assert.DoesNotContain("Tâche archivée", digest);
        Assert.Contains("Rapport de la tâche archivée", digest);

        // The archived task's id still joins the reports.
        JsonObject reportFilter = (JsonObject)SearchBody(handler.Requests[2])["filters"]![0]!;
        Assert.Equal(
            [ActiveTaskId, ArchivedTaskId],
            ((JsonArray)reportFilter["value"]!).Select(n => n!.GetValue<string>()));
    }

    // The v2 search serves a page at a time: the overview follows has_more, and
    // the paging rides in the query string, never in the body.
    [Fact]
    [Trait("Category", "regression")]
    public async Task OverviewListsALinkedTaskBeyondTheFirstSearchPage()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, ProjectRead())
            .Reply(HttpStatusCode.OK, SearchPage(true, TaskRow(ActiveTaskId, "Tâche récente")))
            .Reply(HttpStatusCode.OK, SearchPage(false, TaskRow(ArchivedTaskId, "Tâche ancienne")))
            .Reply(HttpStatusCode.OK, SearchPage(false));

        string digest = await NewReader(handler).OverviewAsync(ProjectId, Ct);

        Assert.Contains("Tâche récente", digest);
        Assert.Contains("Tâche ancienne", digest);
        Assert.EndsWith("offset=0", handler.Requests[1].PathAndQuery);
        Assert.EndsWith("offset=1", handler.Requests[2].PathAndQuery);
        Assert.False(SearchBody(handler.Requests[1]).ContainsKey("offset"));
    }

    [Fact]
    public async Task OverviewWithoutTasksAsksForNoReports()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, ProjectRead())
            .Reply(HttpStatusCode.OK, SearchPage(false));

        string digest = await NewReader(handler).OverviewAsync(ProjectId, Ct);

        Assert.EndsWith("Aucune tâche.", digest);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OverviewShowsTheCanonicalProjectCompletionSignal()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, ProjectRead(done: true))
            .Reply(HttpStatusCode.OK, SearchPage(false));

        string digest = await NewReader(handler).OverviewAsync(ProjectId, Ct);

        Assert.StartsWith($"id : {ProjectId}\n[x] Refonte Anytype", digest);
    }

    static JsonObject ProjectRow(string name, string? etat, bool done = false, bool archived = false) =>
        Row(ProjectId, name, "project", new JsonObject
        {
            ["etat"] = etat is null ? null : new JsonArray(etat),
            ["done"] = done ? true : null,
            ["archive"] = archived ? true : null,
        });

    [Fact]
    public async Task ListShowsTheCanonicalProjectCompletionSignal()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, SearchPage(false, ProjectRow("Refonte Anytype", "Ouvert", done: true)));

        string digest = await NewReader(handler).ListAsync(ct: Ct);

        Assert.Contains("[x] Refonte Anytype", digest);
    }

    [Fact]
    public async Task ListGroupsByEtatSkipsArchivedAndNamesTheUnknownOnes()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, SearchPage(false,
            ProjectRow("En vol", "En cours"),
            ProjectRow("Rangé", "Ouvert", archived: true),
            ProjectRow("Sans étiquette", null)));

        string digest = await NewReader(handler).ListAsync(ct: Ct);

        Assert.Equal("En cours\n[ ] En vol\n\nSans état\n[ ] Sans étiquette", digest);
        Assert.False(SearchBody(Assert.Single(handler.Requests)).ContainsKey("filters"));
    }

    [Fact]
    public async Task ListWithAnEtatFiltersOnTheServerByOptionName()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, SearchPage(false, ProjectRow("En vol", "En cours")));

        string digest = await NewReader(handler).ListAsync("en_cours", Ct);

        Assert.Equal("[ ] En vol", digest);
        JsonObject filter = (JsonObject)Assert.Single((JsonArray)SearchBody(Assert.Single(handler.Requests))["filters"]!)!;
        Assert.Equal(DevSpace.Props.Etat, filter["property"]!.GetValue<string>());
        Assert.Equal("En cours", ((JsonArray)filter["value"]!).Single()!.GetValue<string>());
    }

    [Fact]
    public async Task ListWithNoProjectSaysSo()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, SearchPage(false));

        Assert.Equal("Aucun projet.", await NewReader(handler).ListAsync(ct: Ct));
    }
}