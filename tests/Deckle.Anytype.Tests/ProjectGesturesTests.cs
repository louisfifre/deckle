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
        return new ProjectGestures(client, new NameResolver(client));
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

        // The API ignores the default template unless template_id is named; the
        // creation POST must carry the project type's frozen template id.
        JsonObject created = server.LastBodyFor("POST");
        Assert.Equal(DevSpace.Templates.Project, created["template_id"]!.GetValue<string>());
    }

    // The live list-add endpoint answers 200 with a bare JSON string, not an
    // object (measured 2026-06-12). Creating a project inside an epic must
    // treat that as success — it used to throw "non-object JSON root" AFTER
    // the membership was already posted.
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

        // The gesture completes and reports the membership.
        Assert.Contains("Ajouté à l'epic Deckle", digest);

        // The membership POST carried exactly the created project's id.
        JsonObject added = server.LastBodyFor("POST");
        var objects = Assert.IsType<JsonArray>(added["objects"]);
        string id = Assert.Single(objects)!.GetValue<string>();
        Assert.Equal(ProjectId, id);
    }

    [Fact]
    public async Task OverviewOmitsArchivedTasksButKeepsTheirReports()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ProjectId, ProjectObject());
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray
            {
                TaskHit(ActiveTaskId, "Tâche active", archived: false),
                TaskHit(ArchivedTaskId, "Tâche archivée", archived: true),
            },
        });
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray
            {
                ReportHit("Rapport actif", ActiveTaskId),
                ReportHit("Rapport archivé", ArchivedTaskId),
            },
        });

        string digest = await NewGestures(server).OverviewAsync(ProjectId, Ct);

        Assert.Contains("Tâche active", digest);
        Assert.Contains("Rapport actif", digest);
        Assert.DoesNotContain("Tâche archivée", digest);
        Assert.Contains("Rapport archivé", digest);
    }

    // The live search serves one page at a time (100 by default, 1000 at most,
    // last-modified first). Once the space held more tasks than a page, a task
    // untouched for a while never reached the overview although its
    // relation_projet named the project (measured 2026-09-10). The overview
    // must walk the pages to the end, and paging rides in the query string —
    // the search body has no paging field.
    [Fact]
    [Trait("Category", "regression")]
    public async Task OverviewListsALinkedTaskBeyondTheFirstSearchPage()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ProjectId, ProjectObject());
        // Task page 1: another project's task only, more pages announced.
        server.OnSearch(SearchPage(hasMore: true,
            TaskHit(ActiveTaskId, "Tâche d'un autre projet", archived: false, project: OtherProjectId)));
        // Task page 2, the last: the old linked task.
        server.OnSearch(SearchPage(hasMore: false,
            TaskHit(OldTaskId, "Tâche ancienne", archived: false)));
        // Reports: one empty page.
        server.OnSearch(SearchPage(hasMore: false));

        string digest = await NewGestures(server).OverviewAsync(ProjectId, Ct);

        Assert.Contains("Tâche ancienne", digest);
        Assert.DoesNotContain("Tâche d'un autre projet", digest);

        // Two task pages, then the single report page.
        var searches = server.Requests.Where(r => r.Method == "POST" && r.Path.EndsWith("/search")).ToList();
        Assert.Equal(3, searches.Count);

        // Each page starts where the previous one ended, at the same size.
        (int offset1, int limit1) = Paging(searches[0].Query);
        (int offset2, int limit2) = Paging(searches[1].Query);
        Assert.Equal(0, offset1);
        Assert.Equal(offset1 + limit1, offset2);
        Assert.Equal(limit1, limit2);

        // The body carries no paging field — the live API would drop it silently.
        JsonObject body = (JsonObject)JsonNode.Parse(searches[0].Body)!;
        Assert.False(body.ContainsKey("limit"));
        Assert.False(body.ContainsKey("offset"));
    }

    [Fact]
    public async Task OverviewShowsTheCanonicalProjectCompletionSignal()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ProjectId, ProjectObject(done: true));
        server.OnSearch(new JsonObject { ["data"] = new JsonArray() });

        string digest = await NewGestures(server).OverviewAsync(ProjectId, Ct);

        Assert.StartsWith("[x] Refonte Anytype", digest);
    }

    [Fact]
    public async Task ListShowsTheCanonicalProjectCompletionSignal()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray(ProjectHit(done: true)),
        });

        string digest = await NewGestures(server).ListAsync(ct: Ct);

        Assert.Contains("[x] Refonte Anytype", digest);
    }

    static JsonObject ProjectObject(bool done = false) => new()
    {
        ["object"] = ProjectHit(done),
    };

    static JsonObject ProjectHit(bool done) => new()
    {
        ["id"] = ProjectId,
        ["name"] = "Refonte Anytype",
        ["type"] = new JsonObject { ["key"] = DevSpace.Types.Project },
        ["properties"] = new JsonArray
        {
            new JsonObject
            {
                ["key"] = DevSpace.Props.Done,
                ["checkbox"] = done,
            },
        },
    };

    static JsonObject SearchPage(bool hasMore, params JsonObject[] hits) => new()
    {
        ["data"] = new JsonArray(hits),
        ["pagination"] = new JsonObject { ["has_more"] = hasMore },
    };

    // (offset, limit) as the query string carries them.
    static (int Offset, int Limit) Paging(string query)
    {
        var parameters = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(kv => kv[0], kv => kv[1], StringComparer.Ordinal);
        return (int.Parse(parameters["offset"]), int.Parse(parameters["limit"]));
    }

    static JsonObject TaskHit(string id, string name, bool archived, string? project = null) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
        ["properties"] = new JsonArray
        {
            new JsonObject
            {
                ["key"] = DevSpace.Props.RelationProjet,
                ["objects"] = new JsonArray(project ?? ProjectId),
            },
            new JsonObject
            {
                ["key"] = DevSpace.Props.Archive,
                ["checkbox"] = archived,
            },
        },
    };

    static JsonObject ReportHit(string body, string taskId) => new()
    {
        ["id"] = "bafyreiReportaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        ["type"] = new JsonObject { ["key"] = DevSpace.Types.Rapport },
        ["markdown"] = body,
        ["properties"] = new JsonArray
        {
            new JsonObject
            {
                ["key"] = DevSpace.Props.TachesLiees,
                ["objects"] = new JsonArray(taskId),
            },
            new JsonObject
            {
                ["key"] = DevSpace.Props.DateDuJournal,
                ["date"] = "2026-07-27T00:00:00Z",
            },
        },
    };
}
