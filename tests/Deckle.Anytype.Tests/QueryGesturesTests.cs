using System.Net;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for QueryGestures over the shared FakeAnytypeServer. A select
// value must resolve to an EXISTING option (frozen vocabularies in DevSpace, an
// unknown value throwing before any PATCH), and the unmapped « tag » property is
// refused outright. The free-vocabulary live-resolution path is exercised in
// LiveTagResolverTests, since no mapped property routes there.
//
// Selector is a bafy* id so the resolver short-circuits (no /search route).
[Trait("Category", "integration")]
public class QueryGesturesTests
{
    const string TaskId = "bafyreiTaskaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string ProjectId = "bafyreiprojectaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string EpicId = "bafyreiepicaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static QueryGestures NewGestures(FakeAnytypeServer server, FakeV2Handler? v2 = null)
    {
        var client = new AnytypeApiClient(server.Credentials);
        var v2Client = new AnytypeV2Client(
            new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"),
            null,
            v2 ?? new FakeV2Handler());
        return new QueryGestures(client, new NameResolver(client), v2Client);
    }

    static JsonObject TaskObject() => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = TaskId,
            ["name"] = "Ma tâche",
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
            ["properties"] = new JsonArray(),
        },
    };

    // A rapport GET response — the type that does NOT carry the « Archivé »
    // checkbox, so archive must refuse it.
    static JsonObject RapportObject() => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = TaskId,
            ["name"] = "Un rapport",
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Rapport },
            ["properties"] = new JsonArray(),
        },
    };

    static JsonObject ProjectObject() => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = ProjectId,
            ["name"] = "Deckle",
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Project },
            ["properties"] = new JsonArray(),
        },
    };

    // ── CompleteAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteMarksATaskWithTheCanonicalDoneCheckbox()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        string digest = await NewGestures(server).CompleteAsync(TaskId, ct: Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        JsonObject done = Assert.IsType<JsonObject>(
            Assert.Single((JsonArray)patched["properties"]!));
        Assert.Equal(DevSpace.Props.Done, done["key"]!.GetValue<string>());
        Assert.True(done["checkbox"]!.GetValue<bool>());
        Assert.Contains("Tâche terminée", digest);
    }

    [Fact]
    public async Task CompleteReopensAProjectThroughTheSameCanonicalSignal()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ProjectId, ProjectObject());
        server.OnPatchObject(ProjectId, ProjectObject());

        string digest = await NewGestures(server)
            .CompleteAsync(ProjectId, value: false, ct: Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        JsonObject done = Assert.IsType<JsonObject>(
            Assert.Single((JsonArray)patched["properties"]!));
        Assert.Equal(DevSpace.Props.Done, done["key"]!.GetValue<string>());
        Assert.False(done["checkbox"]!.GetValue<bool>());
        Assert.Contains("Chantier rouvert", digest);
    }

    static JsonObject EpicObject() => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = EpicId,
            ["name"] = "Applications",
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Epic },
            ["properties"] = new JsonArray(),
        },
    };

    // ── SearchAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchWithContextSurfacesProjectAndTaskFramingBeforeTheSnippet()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray
            {
                SearchHit(
                    ProjectId,
                    "Deckle",
                    DevSpace.Types.Project,
                    "Details\nEnjeu\nConstruire l’utilitaire Windows local.",
                    TextProperty(DevSpace.Props.Description, "Utilitaire Windows local."),
                    TextProperty(DevSpace.Props.DefinitionDeFini, "Le parcours principal est livré.")),
                SearchHit(
                    TaskId,
                    "Reconnexion",
                    DevSpace.Types.Task,
                    "Details\nObjet\nReprendre après une veille.",
                    TextProperty(DevSpace.Props.Description, "Reprendre après la veille."),
                    TextProperty(DevSpace.Props.DefinitionDeFini, "Aucune action manuelle requise.")),
            },
        });

        string digest = await NewGestures(server).SearchAsync("Deckle", context: true, ct: Ct);

        Assert.Equal(
            "project · Deckle\n" +
            "Description : Utilitaire Windows local.\n" +
            "Définition de fini : Le parcours principal est livré.\n" +
            "Aperçu :\n" +
            "Details\n" +
            "Enjeu\n" +
            "Construire l’utilitaire Windows local.\n" +
            "task · Reconnexion\n" +
            "Description : Reprendre après la veille.\n" +
            "Définition de fini : Aucune action manuelle requise.\n" +
            "Aperçu :\n" +
            "Details\n" +
            "Objet\n" +
            "Reprendre après une veille.",
            digest);
    }

    [Fact]
    public async Task SearchWithContextOmitsEmptyProjectFramingAndLimitsSnippetsToFiveLines()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray
            {
                SearchHit(
                    ProjectId,
                    "Deckle",
                    DevSpace.Types.Project,
                    "Corps du projet",
                    TextProperty(DevSpace.Props.Description, "   "),
                    TextProperty(DevSpace.Props.DefinitionDeFini, "")),
                SearchHit(
                    EpicId,
                    "Applications",
                    DevSpace.Types.Epic,
                    snippet: "Ligne 1\nLigne 2\nLigne 3\nLigne 4\nLigne 5\nLigne 6"),
            },
        });

        string digest = await NewGestures(server).SearchAsync("Deckle", context: true, ct: Ct);

        Assert.Equal(
            "project · Deckle\n" +
            "Aperçu :\n" +
            "Corps du projet\n" +
            "epic · Applications\n" +
            "Aperçu :\n" +
            "Ligne 1\n" +
            "Ligne 2\n" +
            "Ligne 3 …",
            digest);
    }

    [Fact]
    public async Task SearchWithoutContextReturnsCompactIdentitiesAndNamesReportsFromTheirSnippet()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray
            {
                SearchHit(
                    TaskId,
                    "Reconnexion",
                    DevSpace.Types.Task,
                    "Details\nObjet\nReprendre après une veille.",
                    TextProperty(DevSpace.Props.Description, "Reprendre après la veille."),
                    TextProperty(DevSpace.Props.DefinitionDeFini, "Aucune action manuelle requise.")),
                SearchHit(
                    EpicId,
                    "",
                    DevSpace.Types.Rapport,
                    snippet: "2026-07-21 — Session Anytype\nDécision importante"),
            },
        });

        string digest = await NewGestures(server).SearchAsync("Anytype", ct: Ct);

        Assert.Equal(
            "task · Reconnexion\n" +
            "rapport · 2026-07-21 — Session Anytype",
            digest);
    }

    static JsonObject SearchHit(
        string id,
        string name,
        string typeKey,
        params JsonObject[] properties) => SearchHit(id, name, typeKey, null, properties);

    static JsonObject SearchHit(
        string id,
        string name,
        string typeKey,
        string? snippet = null,
        params JsonObject[] properties)
    {
        var propertyArray = new JsonArray();
        foreach (JsonObject property in properties) propertyArray.Add(property);

        return new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["type"] = new JsonObject { ["key"] = typeKey },
            ["snippet"] = snippet,
            ["properties"] = propertyArray,
        };
    }

    static JsonObject TextProperty(string key, string value) => new()
    {
        ["key"] = key,
        ["format"] = "text",
        ["text"] = value,
    };

    // « tag » is intentionally mapped onto no type, so update must REFUSE it
    // before any write.
    [Fact]
    public async Task UpdateRefusesTheUnmappedTagPropertyAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());

        var props = new JsonObject { ["tag"] = "urgent" };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).UpdateAsync(TaskId, null, props, Ct));

        Assert.Contains("tag", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task UpdateOnFrozenVocabularyResolvesInMemoryWithoutALiveLookup()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());
        // Deliberately register NO /properties or /tags route: a frozen vocabulary
        // must resolve without touching the live options endpoint.

        var props = new JsonObject { ["etat"] = "En cours" };
        await NewGestures(server).UpdateAsync(TaskId, null, props, Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        var entries = Assert.IsType<JsonArray>(patched["properties"]);
        JsonObject entry = Assert.IsType<JsonObject>(Assert.Single(entries));
        Assert.Equal(DevSpace.Props.Etat, entry["key"]!.GetValue<string>());
        Assert.Equal("en_cours", entry["select"]!.GetValue<string>());

        Assert.DoesNotContain(server.Requests, r => r.Path.Contains("/properties"));
    }

    [Fact]
    public async Task UpdateOnFrozenVocabularyWithUnknownValueThrowsAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());

        var props = new JsonObject { ["etat"] = "pas-un-etat" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).UpdateAsync(TaskId, null, props, Ct));

        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // ── UpdateAsync — rename ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateWithNameOnlyPatchesTheTitleAtTheRootWithoutProperties()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        await NewGestures(server).UpdateAsync(TaskId, "Nouveau titre", null, Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        Assert.Equal("Nouveau titre", patched["name"]!.GetValue<string>());
        Assert.False(patched.ContainsKey("properties"));
    }

    [Fact]
    public async Task UpdateWithNameAndPropertiesComposesASinglePatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        var props = new JsonObject { ["etat"] = "En cours" };
        await NewGestures(server).UpdateAsync(TaskId, "Nouveau titre", props, Ct);

        Assert.Equal(1, server.Requests.Count(r => r.Method == "PATCH"));
        JsonObject patched = server.LastBodyFor("PATCH");
        Assert.Equal("Nouveau titre", patched["name"]!.GetValue<string>());
        JsonObject entry = Assert.IsType<JsonObject>(Assert.Single((JsonArray)patched["properties"]!));
        Assert.Equal(DevSpace.Props.Etat, entry["key"]!.GetValue<string>());
        Assert.Equal("en_cours", entry["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateWithABlankNameThrowsAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).UpdateAsync(TaskId, "   ", null, Ct));

        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task UpdateWithNeitherNameNorPropertiesThrowsAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).UpdateAsync(TaskId, null, null, Ct));

        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task UpdateRequiresAStableObjectIdBeforeReadingOrWriting()
    {
        using var server = new FakeAnytypeServer();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).UpdateAsync("Ancien titre", "Nouveau titre", null, Ct));

        Assert.Contains("id Anytype stable", error.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task UpdateRenamingARapportIsRefusedAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, RapportObject());

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).UpdateAsync(TaskId, "Un autre titre", null, Ct));

        Assert.Contains("replace_section", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // ── ArchiveAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveSetsTheArchivedCheckboxTrue()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        await NewGestures(server).ArchiveAsync(TaskId, ct: Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        JsonObject entry = Assert.IsType<JsonObject>(((JsonArray)patched["properties"]!).Single());
        Assert.Equal(DevSpace.Props.Archive, entry["key"]!.GetValue<string>());
        Assert.True(entry["checkbox"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ArchiveWithValueFalseRestoresTheObject()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        await NewGestures(server).ArchiveAsync(TaskId, value: false, ct: Ct);

        JsonObject patched = server.LastBodyFor("PATCH");
        JsonObject entry = Assert.IsType<JsonObject>(((JsonArray)patched["properties"]!).Single());
        Assert.False(entry["checkbox"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ArchiveOnARapportIsRefusedAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, RapportObject());

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).ArchiveAsync(TaskId, ct: Ct));

        Assert.Contains("Archivé", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // A write never settles for the object a name merely resembles: a lone hit
    // that is not named exactly is refused with its id, and nothing is patched.
    [Fact]
    [Trait("Category", "regression")]
    public async Task ArchiveByANameThatOnlyResemblesAnObjectIsRefusedAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray(new JsonObject
            {
                ["id"] = TaskId,
                ["name"] = "Ma tâche de test",
                ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
            }),
        });

        InexactNameException ex = await Assert.ThrowsAsync<InexactNameException>(
            () => NewGestures(server).ArchiveAsync("Ma tâche", ct: Ct));

        Assert.Contains(TaskId, ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task CompleteByANameThatOnlyResemblesAnObjectIsRefusedAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray(new JsonObject
            {
                ["id"] = TaskId,
                ["name"] = "Ma tâche de test",
                ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
            }),
        });

        await Assert.ThrowsAsync<InexactNameException>(
            () => NewGestures(server).CompleteAsync("Ma tâche", ct: Ct));

        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task ArchiveByTheExactNameStillWorks()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = ProjectId,
                    ["name"] = "Ma tâche de test",
                    ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
                },
                new JsonObject
                {
                    ["id"] = TaskId,
                    ["name"] = "Ma tâche",
                    ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
                }),
        });
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        await NewGestures(server).ArchiveAsync("Ma tâche", ct: Ct);

        Assert.Contains(server.Requests, r => r.Method == "PATCH" && r.Path.EndsWith(TaskId));
    }

    // ── LinkAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LinkProjectToEpicAddsTheProjectToTheEpicCollectionWithoutPatchingTheProject()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(ProjectId, ProjectObject());
        server.OnGetObject(EpicId, EpicObject());
        server.OnPostListObjects(EpicId, "\"Objects added successfully\"");

        string digest = await NewGestures(server).LinkAsync(ProjectId, [EpicId], Ct);

        Assert.Contains("rattachement", digest);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");

        JsonObject posted = server.LastBodyFor("POST");
        JsonArray objects = Assert.IsType<JsonArray>(posted["objects"]);
        string id = Assert.Single(objects)!.GetValue<string>();
        Assert.Equal(ProjectId, id);
    }

    // ── ReplaceSectionAsync ─────────────────────────────────────────────────────
    //
    // replace_section works on the v2 block document through FakeV2Handler: one
    // read of the blocks, one atomic PATCH of delete_block / insert_blocks ops,
    // one markdown read to verify. The v1 server only sits behind the name
    // resolver, which a bafy* id short-circuits, so every test expects it to see
    // no request at all.

    const string ObjectPath = "/v2/spaces/space-1/objects/" + TaskId;
    const string ETag = "\"e1\"";

    static JsonObject Block(string id, string type, string? text = null, int indent = 0)
    {
        var block = new JsonObject { ["id"] = id, ["type"] = type, ["indent"] = indent };
        if (text is not null) block["text"] = text;
        return block;
    }

    // A v2 object read: the title as a property and the blocks in reading order.
    static string V2Doc(params JsonObject[] blocks) => new JsonObject
    {
        ["properties"] = new JsonObject { ["name"] = "Ma tâche" },
        ["blocks"] = new JsonArray(blocks.Select(b => (JsonNode)b.DeepClone()).ToArray()),
    }.ToJsonString();

    // The ?format=md read, in the shape the survey measured: a JSON object whose
    // "markdown" field holds the text.
    static string MdRead(string markdown) => new JsonObject
    {
        ["etag"] = "e2",
        ["id"] = TaskId,
        ["type"] = "task",
        ["markdown"] = markdown,
    }.ToJsonString();

    // The three replies of a replace: the block read, the accepted write, the
    // markdown read-back.
    static FakeV2Handler ReplaceFlow(JsonObject[] blocks, string markdownAfter, string? etag = ETag)
    {
        var handler = new FakeV2Handler();
        if (etag is null) handler.Reply(HttpStatusCode.OK, V2Doc(blocks));
        else handler.Reply(HttpStatusCode.OK, V2Doc(blocks), ("ETag", etag));
        return handler
            .Reply(HttpStatusCode.OK, "{}")
            .Reply(HttpStatusCode.OK, MdRead(markdownAfter));
    }

    static FakeV2Handler.Seen SentPatch(FakeV2Handler handler)
        => Assert.Single(handler.Requests, r => r.Method == "PATCH");

    // The ops of the single PATCH, one short line each.
    static string[] SentOps(FakeV2Handler handler)
    {
        FakeV2Handler.Seen patch = SentPatch(handler);
        Assert.Equal(ObjectPath, patch.PathAndQuery);
        return ((JsonArray)JsonNode.Parse(patch.Body!)!["ops"]!)
            .Select(node => (JsonObject)node!)
            .Select(op => op["op"]!.GetValue<string>() switch
            {
                "delete_block" => $"delete {op["id"]} recursive={op["recursive"]}",
                "insert_blocks" => $"insert after {op["after"]}: {op["markdown"]}",
                var other => other,
            })
            .ToArray();
    }

    // Title, a "État" section, a "Notes" section.
    static JsonObject[] ThreeSections() =>
    [
        Block("t", "heading_1", "Tâche"),
        Block("h1", "heading_2", "État"),
        Block("a1", "paragraph", "À faire."),
        Block("h2", "heading_2", "Notes"),
        Block("b1", "paragraph", "rien"),
    ];

    // Anytype's markdown render: GFM hard-break trailing spaces, underscores
    // escaped.
    const string ThreeSectionsReplaced =
        "# Tâche   \n\n## État   \nEn cours sur module\\_X.   \n\n## Notes   \nrien   ";

    // A middle section: the blocks under the heading are deleted (recursively,
    // by id), the new content is inserted as markdown right after the heading,
    // all in one batch conditioned on the ETag of the read. The verification
    // read compares through Anytype's escaping.
    [Fact]
    public async Task ReplaceSectionDeletesTheBlocksUnderTheHeadingAndInsertsTheContentAfterIt()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = ReplaceFlow(ThreeSections(), ThreeSectionsReplaced);

        string digest = await NewGestures(server, v2)
            .ReplaceSectionAsync(TaskId, "État", "En cours sur module_X.", Ct);

        Assert.Equal(
            ["delete a1 recursive=true", "insert after h1: En cours sur module_X."],
            SentOps(v2));
        Assert.Equal(ETag, SentPatch(v2).Header("If-Match"));
        Assert.Equal(["GET", "PATCH", "GET"], v2.Requests.Select(r => r.Method));
        Assert.EndsWith("format=md", v2.Requests[2].PathAndQuery);
        Assert.Equal("Section « État » remplacée dans Ma tâche (vérifié).", digest);
        Assert.Empty(server.Requests);
    }

    // No ETag on the read means no condition on the write.
    [Fact]
    public async Task ReplaceSectionSendsNoIfMatchWhenTheReadGaveNoETag()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = ReplaceFlow(ThreeSections(), ThreeSectionsReplaced, etag: null);

        await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "En cours sur module_X.", Ct);

        Assert.False(SentPatch(v2).HasHeader("If-Match"));
    }

    // The last section runs to the end of the document.
    [Fact]
    public async Task ReplaceSectionOnTheLastSectionRunsToTheEndOfTheDocument()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            .. ThreeSections(),
            Block("b2", "paragraph", "encore"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "# Tâche\n## État\nÀ faire.\n## Notes\nfini");

        await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "Notes", "fini", Ct);

        Assert.Equal(
            ["delete b1 recursive=true", "delete b2 recursive=true", "insert after h2: fini"],
            SentOps(v2));
    }

    // A section inside a column ends with the column, not with the next heading
    // of the document: the block after the container is not in it.
    [Fact]
    public async Task ReplaceSectionInsideAColumnStopsAtTheEndOfTheContainer()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("r", "row"),
            Block("c", "column", indent: 1),
            Block("h1", "heading_2", "Colonne", indent: 2),
            Block("a1", "paragraph", "x", indent: 2),
            Block("z", "paragraph", "hors de la colonne"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "## Colonne\ny\nhors de la colonne");

        await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "Colonne", "y", Ct);

        Assert.Equal(["delete a1 recursive=true", "insert after h1: y"], SentOps(v2));
    }

    // Blocks nested deeper than the section's own level go with the sibling
    // above: only the top-level blocks are named, and the recursive delete takes
    // the rest.
    [Fact]
    public async Task ReplaceSectionDeletesOnlyTheTopLevelBlocksOfTheSection()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("h1", "heading_2", "État"),
            Block("a1", "paragraph", "avant"),
            Block("r", "row"),
            Block("c", "column", indent: 1),
            Block("n", "paragraph", "dans la colonne", indent: 2),
            Block("a2", "paragraph", "après"),
            Block("h2", "heading_2", "Notes"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "## État\nneuf\n## Notes");

        await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "neuf", Ct);

        Assert.Equal(
            [
                "delete a1 recursive=true",
                "delete r recursive=true",
                "delete a2 recursive=true",
                "insert after h1: neuf",
            ],
            SentOps(v2));
    }

    // Blocks nested under the heading itself (a toggle heading, say) are part of
    // its section: they have no sibling above to be deleted with.
    [Fact]
    public async Task ReplaceSectionDeletesTheBlocksNestedUnderTheHeadingItself()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("h1", "heading_2", "État"),
            Block("k1", "paragraph", "enfant", indent: 1),
            Block("k2", "paragraph", "autre enfant", indent: 1),
            Block("h2", "heading_2", "Notes"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "## État\nneuf\n## Notes");

        await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "neuf", Ct);

        Assert.Equal(
            ["delete k1 recursive=true", "delete k2 recursive=true", "insert after h1: neuf"],
            SentOps(v2));
    }

    // A deeper heading belongs to the section and is removed with it, and the
    // verification does not read its disappearance as loss.
    [Fact]
    public async Task ReplaceSectionRemovesADeeperHeadingInsideTheSectionAndStillVerifies()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("g", "heading_2", "Groupe"),
            Block("e", "heading_3", "enfant"),
            Block("x", "paragraph", "x"),
            Block("f", "heading_2", "Fin"),
            Block("z", "paragraph", "z"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "## Groupe   \nrésumé   \n## Fin   \nz   ");

        string digest = await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "Groupe", "résumé", Ct);

        Assert.Equal(
            ["delete e recursive=true", "delete x recursive=true", "insert after g: résumé"],
            SentOps(v2));
        Assert.Contains("(vérifié)", digest);
    }

    // The next heading of the same or a higher level ends the section; a lower
    // one does not.
    [Theory]
    [InlineData("heading_2", true)]
    [InlineData("heading_1", true)]
    [InlineData("heading_3", false)]
    public async Task ReplaceSectionEndsAtASameOrHigherLevelHeadingOnly(string nextType, bool endsThere)
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("h", "heading_2", "État"),
            Block("a", "paragraph", "dedans"),
            Block("n", nextType, "Suite"),
            Block("b", "paragraph", "dessous"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "## État\nneuf");

        await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "neuf", Ct);

        string[] expected = endsThere
            ? ["delete a recursive=true", "insert after h: neuf"]
            : ["delete a recursive=true", "delete n recursive=true", "delete b recursive=true", "insert after h: neuf"];
        Assert.Equal(expected, SentOps(v2));
    }

    // The schema enum carries both heading_4 and header_4; the fourth level is
    // read from either spelling.
    [Fact]
    public async Task ReplaceSectionReadsTheFourthHeadingLevelFromHeader4()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("h", "header_4", "Détail"),
            Block("a", "paragraph", "dedans"),
            Block("n", "heading_3", "Suite"),
            Block("b", "paragraph", "dessous"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "#### Détail\nneuf\n### Suite\ndessous");

        await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "Détail", "neuf", Ct);

        Assert.Equal(["delete a recursive=true", "insert after h: neuf"], SentOps(v2));
    }

    // Dividers right before the next heading are layout between sections: they
    // stay (the markdown flow deleted them), and the verification looks past
    // them. A divider in the middle of the section is content and goes.
    [Fact]
    [Trait("Category", "regression")]
    public async Task ReplaceSectionKeepsTheDividersRightBeforeTheNextHeading()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("h1", "heading_2", "État"),
            Block("a1", "paragraph", "un"),
            Block("m", "divider"),
            Block("a2", "paragraph", "deux"),
            Block("d1", "divider"),
            Block("d2", "divider"),
            Block("h2", "heading_2", "Notes"),
            Block("b", "paragraph", "rien"),
        ];
        FakeV2Handler v2 = ReplaceFlow(
            blocks, "## État   \nneuf   \n --- \n --- \n## Notes   \nrien   ");

        string digest = await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "neuf", Ct);

        Assert.Equal(
            [
                "delete a1 recursive=true",
                "delete m recursive=true",
                "delete a2 recursive=true",
                "insert after h1: neuf",
            ],
            SentOps(v2));
        Assert.Contains("(vérifié)", digest);
    }

    // At the end of the container there is no next section to separate from, so
    // a trailing divider is part of the section.
    [Fact]
    public async Task ReplaceSectionTakesATrailingDividerAtTheEndOfTheDocument()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("h1", "heading_2", "Notes"),
            Block("a1", "paragraph", "un"),
            Block("d1", "divider"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "## Notes\nneuf");

        await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "Notes", "neuf", Ct);

        Assert.Equal(
            ["delete a1 recursive=true", "delete d1 recursive=true", "insert after h1: neuf"],
            SentOps(v2));
    }

    // Anytype serves the text as markup source (`get\_task`); the caller types
    // the heading naturally, and the match ignores case.
    [Fact]
    public async Task ReplaceSectionMatchesAnEscapedHeadingTextUnescapedAndCaseInsensitive()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("h1", "heading_2", "Module\\_X"),
            Block("a1", "paragraph", "un"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "## Module\\_X\nneuf");

        await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "module_x", "neuf", Ct);

        Assert.Equal(["delete a1 recursive=true", "insert after h1: neuf"], SentOps(v2));
    }

    // Blank content empties the section: deletes only, no insert (an insert of
    // nothing is refused by the schema).
    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task ReplaceSectionWithBlankContentOnlyDeletes(string content)
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = ReplaceFlow(ThreeSections(), "# Tâche\n## État\n## Notes\nrien");

        string digest = await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", content, Ct);

        Assert.Equal(["delete a1 recursive=true"], SentOps(v2));
        Assert.Contains("(vérifié)", digest);
    }

    // An empty section replaced by nothing has no op to send: no write at all,
    // and the answer still comes from the read-back.
    [Fact]
    public async Task ReplaceSectionOfAnEmptySectionWithBlankContentWritesNothing()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("h1", "heading_2", "État"),
            Block("h2", "heading_2", "Notes"),
        ];
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Doc(blocks), ("ETag", ETag))
            .Reply(HttpStatusCode.OK, MdRead("## État\n## Notes"));

        string digest = await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "", Ct);

        Assert.DoesNotContain(v2.Requests, r => r.Method == "PATCH");
        Assert.Contains("(vérifié)", digest);
    }

    [Fact]
    public async Task ReplaceSectionWithAnAbsentHeadingThrowsAndWritesNothing()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = ReplaceFlow(ThreeSections(), "");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, v2).ReplaceSectionAsync(TaskId, "Absent", "x", Ct));

        Assert.Contains("introuvable", ex.Message);
        Assert.Contains("« Tâche », « État », « Notes »", ex.Message);
        Assert.Equal(["GET"], v2.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task ReplaceSectionWithARepeatedHeadingThrowsAmbiguousAndWritesNothing()
    {
        using var server = new FakeAnytypeServer();
        JsonObject[] blocks =
        [
            Block("h1", "heading_2", "Doublon"),
            Block("a", "paragraph", "a"),
            Block("h2", "heading_3", "Doublon"),
            Block("b", "paragraph", "b"),
        ];
        FakeV2Handler v2 = ReplaceFlow(blocks, "");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, v2).ReplaceSectionAsync(TaskId, "Doublon", "x", Ct));

        Assert.Contains("ambig", ex.Message);
        Assert.Contains("2 titres", ex.Message);
        Assert.Equal(["GET"], v2.Requests.Select(r => r.Method));
    }

    // If the object changed since the read, the planned deletes may no longer
    // name this section: the server refuses the whole batch (409), and the
    // caller is told to retry. Nothing was written, and no read-back follows.
    [Fact]
    public async Task ReplaceSectionTurnsAStaleETagIntoARetryRefusal()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Doc(ThreeSections()), ("ETag", ETag))
            .Reply(HttpStatusCode.Conflict, """{"status":409,"code":"etag_mismatch","message":"stale"}""");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "neuf", Ct));

        Assert.Contains("a changé depuis la lecture", ex.Message);
        Assert.Contains("rien n'a été écrit", ex.Message);
        Assert.Contains("Relance", ex.Message);
        Assert.Equal(["GET", "PATCH"], v2.Requests.Select(r => r.Method));
    }

    // A section too long for one atomic batch is refused up front, rather than
    // split into batches that would leave it half replaced.
    [Fact]
    public async Task ReplaceSectionRefusesASectionTooLongForOneBatch()
    {
        using var server = new FakeAnytypeServer();
        var blocks = new List<JsonObject> { Block("h1", "heading_2", "Long") };
        for (int i = 0; i < 512; i++) blocks.Add(Block($"p{i}", "paragraph", "ligne"));
        FakeV2Handler v2 = ReplaceFlow([.. blocks], "");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server, v2).ReplaceSectionAsync(TaskId, "Long", "x", Ct));

        Assert.Contains("trop longue", ex.Message);
        Assert.Equal(["GET"], v2.Requests.Select(r => r.Method));
    }

    // The write is committed but the read-back still shows the old content: the
    // gesture reports the divergence instead of claiming success.
    [Fact]
    public async Task ReplaceSectionReportsDivergenceWhenTheReadBackDoesNotConfirmTheIntent()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = ReplaceFlow(ThreeSections(), "# Tâche\n## État\nancien\n## Notes\nrien");

        string digest = await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "neuf", Ct);

        Assert.Equal(
            "Section « État » écrite dans Ma tâche, mais vérification en échec : " +
            "le contenu relu ne correspond pas à l'intention.",
            digest);
    }

    // The heading after the replaced section is gone from the read-back, so its
    // content now reads as part of the section: the lost heading is what the
    // gesture reports, not the content mismatch it causes.
    [Fact]
    public async Task ReplaceSectionReportsALostHeadingOutsideTheSection()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = ReplaceFlow(ThreeSections(), "# Tâche\n## État\nneuf\nrien");

        string digest = await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "neuf", Ct);

        Assert.Equal(
            "Section « État » écrite dans Ma tâche, mais vérification en échec : " +
            "une autre section a disparu.",
            digest);
    }

    // The batch is committed when the read-back times out: the gesture says the
    // check failed, never that the write did, so the caller does not redo it.
    [Fact]
    [Trait("Category", "regression")]
    public async Task ReplaceSectionReportsAReadBackTimeoutAsAFailedCheck()
    {
        using var server = new FakeAnytypeServer();
        var v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, V2Doc(ThreeSections()), ("ETag", ETag))
            .Reply(HttpStatusCode.OK, "{}")
            .Fail(new TaskCanceledException("timeout"));

        string digest = await NewGestures(server, v2).ReplaceSectionAsync(TaskId, "État", "neuf", Ct);

        Assert.Equal(
            "Section « État » écrite dans Ma tâche, mais vérification en échec : " +
            "la relecture a échoué.",
            digest);
    }
}

