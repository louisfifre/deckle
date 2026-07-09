using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "integration")]
public class SchemaAdminGesturesTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static SchemaAdminGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        var aliases = new AnytypeSpaceAliases(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["home"] = FakeAnytypeServer.Space,
            });
        return new SchemaAdminGestures(client, aliases);
    }

    static JsonObject EmptyList() => new() { ["data"] = new JsonArray() };

    static JsonObject Page(JsonArray data, bool hasMore = false) => new()
    {
        ["data"] = data,
        ["pagination"] = new JsonObject { ["has_more"] = hasMore },
    };

    static JsonObject ExistingType(string name = "Pièce", JsonArray? properties = null) => new()
    {
        ["id"] = "type-piece",
        ["key"] = "piece",
        ["name"] = name,
        ["layout"] = "page",
        ["properties"] = properties ?? new JsonArray(),
    };

    static JsonObject ExistingProperty() => new()
    {
        ["id"] = "prop-etat",
        ["key"] = "etat_identification",
        ["name"] = "État d'identification",
        ["format"] = "select",
    };

    static JsonObject ExistingTag() => new()
    {
        ["id"] = "tag-confirme",
        ["name"] = "Confirmé",
        ["color"] = "grey",
    };

    static JsonObject Manifest() => new()
    {
        ["properties"] = new JsonArray
        {
            new JsonObject
            {
                ["key"] = "etat_identification",
                ["name"] = "État d'identification",
                ["format"] = "select",
                ["tags"] = new JsonArray
                {
                    new JsonObject { ["key"] = "confirme", ["name"] = "Confirmé" },
                },
            },
        },
        ["types"] = new JsonArray
        {
            new JsonObject
            {
                ["key"] = "piece",
                ["name"] = "Pièce",
                ["layout"] = "page",
                ["properties"] = new JsonArray { "etat_identification" },
            },
        },
    };

    [Fact]
    public async Task PreviewReportsAdditiveActionsWithoutWriting()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.Contains("create_property", digest);
        Assert.Contains("create_tag", digest);
        Assert.Contains("create_type", digest);
        Assert.Contains("attach_property", digest);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewDoesNotReattachExistingPropertyReferencedById()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType(properties: new JsonArray { "prop-etat" }) }));
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListPropertyTags("prop-etat", EmptyList());

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain("attach_property", digest);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewDoesNotRecreateExistingTagMatchedByName()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListPropertyTags("prop-etat", Page(new JsonArray { ExistingTag() }));

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain("create_tag", digest);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewReadsEveryPaginatedSchemaPage()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray(), hasMore: true));
        server.OnListTypes(Page(new JsonArray { ExistingType(properties: new JsonArray { "prop-etat" }) }));
        server.OnListProperties(Page(new JsonArray(), hasMore: true));
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListPropertyTags("prop-etat", EmptyList());

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain("create_property", digest);
        Assert.DoesNotContain("create_type", digest);
        Assert.DoesNotContain("attach_property", digest);
    }

    [Fact]
    public async Task PreviewReadsEveryPaginatedTagPage()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListPropertyTags("prop-etat", Page(new JsonArray(), hasMore: true));
        server.OnListPropertyTags("prop-etat", Page(new JsonArray { ExistingTag() }));

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain("create_tag", digest);
    }

    [Fact]
    public async Task PreviewRejectsDuplicateTypeKeysBeforeReadingAnytype()
    {
        using var server = new FakeAnytypeServer();
        JsonObject manifest = new()
        {
            ["types"] = new JsonArray
            {
                new JsonObject { ["key"] = "piece", ["name"] = "Pièce" },
                new JsonObject { ["key"] = "piece", ["name"] = "Autre pièce" },
            },
        };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).PreviewAsync("home", manifest, Ct));

        Assert.Contains("Doublon", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task PreviewRejectsUnknownManifestFieldsBeforeReadingAnytype()
    {
        using var server = new FakeAnytypeServer();
        JsonObject manifest = new()
        {
            ["properties"] = new JsonArray
            {
                new JsonObject
                {
                    ["key"] = "etat_identification",
                    ["name"] = "État d'identification",
                    ["format"] = "select",
                    ["unexpected"] = true,
                },
            },
        };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).PreviewAsync("home", manifest, Ct));

        Assert.Contains("Champ inconnu", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task ApplyRequiresConfirmTrueBeforeLookingUpPreview()
    {
        using var server = new FakeAnytypeServer();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).ApplyAsync("home", "deadbeef", confirm: false, Ct));

        Assert.Contains("confirm:true", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task ApplyCreatesMissingSchemaAndAttachesProperties()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnPostProperty(new JsonObject
        {
            ["id"] = "prop-etat",
            ["key"] = "etat_identification",
            ["name"] = "État d'identification",
            ["format"] = "select",
        });
        server.OnPostPropertyTag("prop-etat", new JsonObject
        {
            ["id"] = "tag-confirme",
            ["key"] = "confirme",
            ["name"] = "Confirmé",
            ["color"] = "grey",
        });
        server.OnPostType(new JsonObject
        {
            ["id"] = "type-piece",
            ["key"] = "piece",
            ["name"] = "Pièce",
            ["layout"] = "page",
        });
        server.OnPatchType("type-piece", new JsonObject
        {
            ["id"] = "type-piece",
            ["key"] = "piece",
            ["name"] = "Pièce",
            ["layout"] = "page",
        });

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync("home", Manifest(), Ct);
        string previewId = preview.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];

        string digest = await gestures.ApplyAsync("home", previewId, confirm: true, Ct);

        Assert.Contains("propriété créée etat_identification", digest);
        Assert.Contains("tag créé etat_identification:Confirmé", digest);
        Assert.Contains("type créé piece", digest);
        Assert.Contains("propriétés attachées à piece", digest);

        JsonObject property = server.Requests
            .Where(r => r.Method == "POST" && r.Path.EndsWith("/properties", StringComparison.Ordinal))
            .Select(r => (JsonObject)JsonNode.Parse(r.Body)!)
            .Single();
        Assert.Equal("etat_identification", property["key"]!.GetValue<string>());
        Assert.Equal("select", property["format"]!.GetValue<string>());

        JsonObject typePatch = server.LastBodyFor("PATCH");
        JsonArray refs = Assert.IsType<JsonArray>(typePatch["properties"]);
        Assert.Equal("prop-etat", Assert.Single(refs)!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyDoesNotRenameExistingTypeWhileAttachingProperties()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType(name: "Nom live") }));
        server.OnListProperties(EmptyList());
        server.OnPostProperty(new JsonObject
        {
            ["id"] = "prop-etat",
            ["key"] = "etat_identification",
            ["name"] = "État d'identification",
            ["format"] = "select",
        });
        server.OnPostPropertyTag("prop-etat", new JsonObject
        {
            ["id"] = "tag-confirme",
            ["key"] = "confirme",
            ["name"] = "Confirmé",
            ["color"] = "grey",
        });
        server.OnPatchType("type-piece", ExistingType(name: "Nom live"));

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync("home", Manifest(), Ct);
        string previewId = preview.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];

        await gestures.ApplyAsync("home", previewId, confirm: true, Ct);

        JsonObject typePatch = server.LastBodyFor("PATCH");
        Assert.Equal("Nom live", typePatch["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyRefusesWhenLiveStateNeedsActionsThatWereNotPreviewed()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType(properties: new JsonArray { "prop-etat" }) }));
        server.OnListTypes(EmptyList());
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListProperties(EmptyList());
        server.OnListPropertyTags("prop-etat", Page(new JsonArray { ExistingTag() }));

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync("home", Manifest(), Ct);
        string previewId = preview.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gestures.ApplyAsync("home", previewId, confirm: true, Ct));

        Assert.Contains("Relance schema_preview", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }
}
