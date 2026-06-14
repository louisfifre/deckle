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

    static ProjectGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new ProjectGestures(client, new NameResolver(client));
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
        await NewGestures(server).CreateAsync("Mon projet");

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

        string digest = await NewGestures(server).CreateAsync("Mon projet", epic: EpicId);

        // The gesture completes and reports the membership.
        Assert.Contains("Ajouté à l'epic Deckle", digest);

        // The membership POST carried exactly the created project's id.
        JsonObject added = server.LastBodyFor("POST");
        var objects = Assert.IsType<JsonArray>(added["objects"]);
        string id = Assert.Single(objects)!.GetValue<string>();
        Assert.Equal(ProjectId, id);
    }
}
