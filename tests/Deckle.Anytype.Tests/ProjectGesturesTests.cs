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

    // Creation is on v2: one POST of the object, whose receipt carries the new
    // id. The v1 server only serves the epic's collection (the list-add and the
    // epic's name).
    static ProjectGestures NewCreator(FakeAnytypeServer server, FakeV2Handler handler)
    {
        var client = new AnytypeApiClient(server.Credentials);
        var v2 = new AnytypeV2Client(
            new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"), null, handler);
        return new ProjectGestures(client, new NameResolver(client), v2);
    }

    static FakeV2Handler Created(string id) =>
        new FakeV2Handler().Reply(HttpStatusCode.Created, $"{{\"id\":\"{id}\"}}");

    static JsonObject PostedBody(FakeV2Handler handler) =>
        (JsonObject)JsonNode.Parse(Assert.Single(handler.Requests).Body!)!;

    [Fact]
    public async Task CreateEpicPostsTheEpicWithNoStateAndNoTemplate()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = Created(EpicId);

        string digest = await NewCreator(server, v2).CreateEpicAsync("Deckle", Ct);

        Assert.Equal("Epic créé : Deckle", digest);
        JsonObject created = PostedBody(v2);
        Assert.Equal(DevSpace.Types.Epic, created["type"]!.GetValue<string>());
        Assert.Equal("Deckle", created["name"]!.GetValue<string>());
        // The state is not kept on an epic, and the type's default template
        // applies: neither a template nor a property is sent.
        Assert.False(created.ContainsKey("template"));
        Assert.False(created.ContainsKey("template_id"));
        Assert.False(created.ContainsKey("properties"));
    }

    [Fact]
    public async Task CreateStartsAProjectReadyWithNoTemplate()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = Created(ProjectId);

        string digest = await NewCreator(server, v2).CreateAsync("Mon projet", ct: Ct);

        Assert.Equal($"Projet créé : Mon projet (Prêt)\nid : {ProjectId}", digest);
        JsonObject created = PostedBody(v2);
        Assert.Equal(DevSpace.Types.Project, created["type"]!.GetValue<string>());
        Assert.False(created.ContainsKey("template"));
        JsonObject props = created["properties"]!.AsObject();
        Assert.Equal("Prêt", props[DevSpace.Props.Status]!.AsArray().Single()!.GetValue<string>());
        Assert.False(props.ContainsKey(DevSpace.Props.Done));
        // No epic, so the v1 server is not reached.
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task CreateTakesTheStateThePriorityAndTheBroadDoneDefinition()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = Created(ProjectId);

        await NewCreator(server, v2).CreateAsync(
            "Transcription", state: "doing", priority: 4, doneDefinition: "  La dictée est fiable.  ", ct: Ct);

        JsonObject props = PostedBody(v2)["properties"]!.AsObject();
        Assert.Equal("En cours", props[DevSpace.Props.Status]!.AsArray().Single()!.GetValue<string>());
        Assert.Equal("4", props[DevSpace.Props.Priority]!.AsArray().Single()!.GetValue<string>());
        Assert.Equal("La dictée est fiable.", props[DevSpace.Props.DoneDefinition]!.GetValue<string>());
    }

    [Theory]
    [InlineData("termine")]
    [InlineData("abandonne")]
    [InlineData("en_attente")]
    public async Task CreateRefusesAStateTheV5SpaceHadWithNothingMade(string state)
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = Created(ProjectId);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewCreator(server, v2).CreateAsync("Mon projet", state: state, ct: Ct));

        Assert.Contains("En pause (paused)", ex.Message);
        Assert.Empty(v2.Requests);
    }

    // The live list-add endpoint answers 200 with a bare JSON string, not an
    // object. Creating a project inside an epic must treat that as success:
    // the membership is already posted. The project enters its epic twice, by the
    // `epic` property in the create and by the epic's collection.
    [Fact]
    public async Task CreateInsideAnEpicWritesTheEpicPropertyAndTheCollectionAndSurvivesTheBareStringResponse()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = Created(ProjectId);
        server.OnPostListObjects(EpicId, "\"Objects added successfully\"");
        server.OnGetObject(EpicId, new JsonObject
        {
            ["object"] = new JsonObject { ["id"] = EpicId, ["name"] = "Deckle" },
        });

        string digest = await NewCreator(server, v2).CreateAsync("Mon projet", epic: EpicId, ct: Ct);

        Assert.Contains("Ajouté à l'epic Deckle", digest);

        JsonObject props = PostedBody(v2)["properties"]!.AsObject();
        Assert.Equal(EpicId, props[DevSpace.Props.Epic]!.AsArray().Single()!.GetValue<string>());

        JsonObject added = server.LastBodyFor("POST");
        var objects = Assert.IsType<JsonArray>(added["objects"]);
        string id = Assert.Single(objects)!.GetValue<string>();
        Assert.Equal(ProjectId, id);
    }
    // ── TodayAsync ───────────────────────────────────────────────────────────

    // 2026-11-09, in UTC so the day does not depend on the machine's zone.
    sealed class StoppedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    static ProjectGestures NewTodayReader(FakeV2Handler handler)
    {
        var credentials = new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1");
        var v1 = new AnytypeApiClient(credentials);
        return new ProjectGestures(v1, new NameResolver(v1), new AnytypeV2Client(credentials, null, handler), new StoppedClock());
    }

    static JsonObject DatedTask(string name, string? day, bool done = false, bool archived = false) =>
        Row("bafyreitask" + name.GetHashCode().ToString("x"), name, "task", new JsonObject
        {
            ["due_date"] = day is null ? null : day + "T00:00:00Z",
            ["done"] = done ? true : null,
            ["archive"] = archived ? true : null,
            ["projects"] = new JsonArray(ProjectId),
            ["task_type"] = new JsonArray("Produire"),
            ["priority"] = new JsonArray("4"),
            ["done_definition"] = "Le test passe.",
            ["time_estimated"] = 1.5,
        });

    [Fact]
    public async Task TodayListsTheTasksOfTheDayAndTheLateOnesThenTheProjectsInProgress()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, SearchPage(false,
                Row(ProjectId, "Anytype", "project", new JsonObject
                {
                    ["status"] = new JsonArray("En cours"), ["priority"] = new JsonArray("5"),
                    ["due_date"] = "2026-11-15T00:00:00Z",
                }),
                Row(OtherProjectId, "Dormant", "project", new JsonObject { ["status"] = new JsonArray("Dormant") }),
                Row(EpicId, "Fini", "project", new JsonObject { ["status"] = new JsonArray("En cours"), ["archive"] = true })))
            .Reply(HttpStatusCode.OK, SearchPage(false,
                DatedTask("Aujourd'hui", "2026-11-09"),
                DatedTask("Retard", "2026-11-03"),
                DatedTask("Demain", "2026-11-10"),
                DatedTask("Sans date", null),
                DatedTask("Faite", "2026-11-01", done: true),
                DatedTask("Abandonnée", "2026-11-02", archived: true)));

        string digest = await NewTodayReader(handler).TodayAsync(Ct);

        Assert.Equal(
            "Aujourd'hui, 2026-11-09 :\n\n" +
            "[ ] Retard · en retard, prévue le 2026-11-03\n" +
            "Projet : Anytype\n" +
            "Produire · P4 · 1,5 h estimée(s)\n" +
            "Définition de fini : Le test passe.\n\n" +
            "[ ] Aujourd'hui\n" +
            "Projet : Anytype\n" +
            "Produire · P4 · 1,5 h estimée(s)\n" +
            "Définition de fini : Le test passe.\n\n" +
            "Projets en cours :\n" +
            "Anytype · P5 · cible 2026-11-15",
            digest);
        Assert.All(handler.Requests, r => Assert.Equal("POST", r.Method));
    }

    [Fact]
    public async Task TodaySaysSoWhenNothingIsDue()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, SearchPage(false))
            .Reply(HttpStatusCode.OK, SearchPage(false, DatedTask("Demain", "2026-11-10")));

        string digest = await NewTodayReader(handler).TodayAsync(Ct);

        Assert.Contains("Aucune tâche du jour ni en retard.", digest);
        Assert.Contains("Projets en cours :\nAucun.", digest);
    }

    // ── Reads over the v2 client ─────────────────────────────────────────────

    const string ReportId ="bafyreiReportaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    // The resolver is never reached (selectors are ids) and creation is not
    // exercised here, so the v1 client is a placeholder; only the v2 handler talks.
    static ProjectGestures NewReader(FakeV2Handler handler)
    {
        var credentials = new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1");
        var v1 = new AnytypeApiClient(credentials);
        var v2 = new AnytypeV2Client(credentials, null, handler);
        return new ProjectGestures(v1, new NameResolver(v1), v2);
    }

    static string ProjectRead(bool archived = false) => new JsonObject
    {
        ["id"] = ProjectId,
        ["name"] = "Refonte Anytype",
        ["type"] = "project",
        ["properties"] = new JsonObject
        {
            ["name"] = "Refonte Anytype",
            ["status"] = new JsonArray("Prêt"),
            ["priority"] = new JsonArray("4"),
            ["archive"] = archived ? true : null,
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
        ["priority"] = new JsonArray("2"),
    });

    // A report has no journal-date property in the model v6: its row carries
    // nothing, and its date is the one of its heading.
    static JsonObject ReportRow() => Row(ReportId, "", "report", new JsonObject());

    static string Markdown(string body) =>
        new JsonObject { ["id"] = ReportId, ["type"] = "report", ["markdown"] = body }.ToJsonString();

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
            $"id : {ProjectId}\nRefonte Anytype\nÉtat : Prêt\nPriorité : 4\n"
            + $"Tâches :\n[ ] Tâche active · P2\n"
            + $"Derniers rapports :\n2026-07-27 · {ReportId} — 2026-07-27 11:30 · module_X fini",
            digest);

        FakeV2Handler.Seen tasks = handler.Requests[1];
        Assert.EndsWith("/search?limit=100&offset=0", tasks.PathAndQuery);
        JsonObject taskFilter = (JsonObject)Assert.Single((JsonArray)SearchBody(tasks)["filters"]!)!;
        Assert.Equal(DevSpace.Props.Projects, taskFilter["property"]!.GetValue<string>());
        Assert.Equal(ProjectId, ((JsonArray)taskFilter["value"]!).Single()!.GetValue<string>());

        // One page of the size shown, not every report of the project.
        Assert.EndsWith("/search?limit=3&offset=0", handler.Requests[2].PathAndQuery);
        JsonObject reports = SearchBody(handler.Requests[2]);
        Assert.Equal(DevSpace.Types.Report, reports["type"]!.GetValue<string>());
        JsonObject reportFilter = (JsonObject)Assert.Single((JsonArray)reports["filters"]!)!;
        Assert.Equal(DevSpace.Props.Tasks, reportFilter["property"]!.GetValue<string>());
        Assert.Equal(ActiveTaskId, ((JsonArray)reportFilter["value"]!).Single()!.GetValue<string>());
        // The newest first, by the native creation date.
        JsonObject sort = (JsonObject)Assert.Single((JsonArray)reports["sorts"]!)!;
        Assert.Equal(DevSpace.Native.CreatedDate, sort["property"]!.GetValue<string>());
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
    public async Task OverviewShowsNoDoneBoxOnAProjectAndSaysWhenItIsArchived()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, ProjectRead(archived: true))
            .Reply(HttpStatusCode.OK, SearchPage(false));

        string digest = await NewReader(handler).OverviewAsync(ProjectId, Ct);

        // A project is closed by archiving: its title carries no box.
        Assert.StartsWith($"id : {ProjectId}\nRefonte Anytype\n", digest);
        Assert.DoesNotContain("[x] Refonte", digest);
        Assert.DoesNotContain("[ ] Refonte", digest);
        Assert.Contains("Archivé : oui", digest);
    }

    static JsonObject ProjectRow(string name, string? status, bool archived = false, string? priority = null) =>
        Row(ProjectId, name, "project", new JsonObject
        {
            ["status"] = status is null ? null : new JsonArray(status),
            ["priority"] = priority is null ? null : new JsonArray(priority),
            ["archive"] = archived ? true : null,
        });

    [Fact]
    public async Task ListGroupsByTheFourStatusesInTheModelOrderSkipsArchivedAndNamesTheUnknownOnes()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, SearchPage(false,
            ProjectRow("Dort", "Dormant"),
            ProjectRow("En vol", "En cours", priority: "3"),
            ProjectRow("Rangé", "En cours", archived: true),
            ProjectRow("En pause ici", "En pause"),
            ProjectRow("Prêt ici", "Prêt"),
            ProjectRow("Sans étiquette", null)));

        string digest = await NewReader(handler).ListAsync(ct: Ct);

        Assert.Equal(
            "Prêt\nPrêt ici\n\nEn cours\nEn vol · P3\n\nEn pause\nEn pause ici\n\nDormant\nDort\n\nSans état\nSans étiquette",
            digest);
        Assert.False(SearchBody(Assert.Single(handler.Requests)).ContainsKey("filters"));
    }

    [Fact]
    public async Task ListShowsNoDoneBoxOnAProject()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, SearchPage(false, ProjectRow("Refonte Anytype", "En cours")));

        string digest = await NewReader(handler).ListAsync(ct: Ct);

        Assert.Equal("En cours\nRefonte Anytype", digest);
        // The done box is not even asked for.
        Assert.DoesNotContain("\"done\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task ListWithAStateFiltersOnTheServerByOptionName()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, SearchPage(false, ProjectRow("En vol", "En cours")));

        string digest = await NewReader(handler).ListAsync("doing", Ct);

        Assert.Equal("En vol", digest);
        JsonObject filter = (JsonObject)Assert.Single((JsonArray)SearchBody(Assert.Single(handler.Requests))["filters"]!)!;
        Assert.Equal(DevSpace.Props.Status, filter["property"]!.GetValue<string>());
        Assert.Equal("En cours", ((JsonArray)filter["value"]!).Single()!.GetValue<string>());
    }

    [Theory]
    [InlineData("termine")]
    [InlineData("abandonne")]
    public async Task ListRefusesAStateTheV5SpaceHadAndListsTheFourOthers(string state)
    {
        var handler = new FakeV2Handler();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => NewReader(handler).ListAsync(state, Ct));

        Assert.Contains("Prêt (ready), En cours (doing), En pause (paused), Dormant (dormant)", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ListWithNoProjectSaysSo()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, SearchPage(false));

        Assert.Equal("Aucun projet.", await NewReader(handler).ListAsync(ct: Ct));
    }
}