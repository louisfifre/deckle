using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "integration")]
public class HomeGesturesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    [Fact]
    public async Task CreateDerivesRoomCategoryExistenceAndElementTitle()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        string digest = await Gestures(server).CreateAsync(
            HomeSchema.Types.Outlet,
            [new HomeCreateItem("ZZ-P01", null, new JsonObject { ["Libellé"] = "Prise témoin" })],
            Ct);

        Assert.Contains("ZZ-P01", digest);
        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Outlet, body["type_key"]!.GetValue<string>());
        Assert.Equal("ZZ-P01", body["name"]!.GetValue<string>());
        JsonArray properties = Assert.IsType<JsonArray>(body["properties"]);
        Assert.DoesNotContain(properties.OfType<JsonObject>(), value =>
            value["key"]?.GetValue<string>() == "code");
        JsonNode roomReference = Assert.Single(Assert.IsType<JsonArray>(
            Entry(properties, HomeSchema.Properties.Room)["objects"]))!;
        Assert.Equal("room-zz", roomReference.GetValue<string>());
        Assert.Equal("tag-categorie-p", Entry(properties, HomeSchema.Properties.Category)["select"]!.GetValue<string>());
        Assert.Equal("tag-existence-existant", Entry(properties, HomeSchema.Properties.Existence)["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateAddsTheCreatedObjectToCollectionsOutsideItsProperties()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Collection("floor-1", "Niveau fictif"));

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Room,
            [new HomeCreateItem("ZZ", "Pièce fictive", null, ["Niveau fictif"])],
            Ct);

        FakeHomeAnytypeServer.Received membership = server.Requests.Single(request =>
            request.Method == "POST" && request.Path.EndsWith("/lists/floor-1/objects", StringComparison.Ordinal));
        JsonObject body = (JsonObject)JsonNode.Parse(membership.Body)!;
        Assert.Equal("created-1", Assert.Single(Assert.IsType<JsonArray>(body["objects"]))!.GetValue<string>());

        JsonObject create = (JsonObject)JsonNode.Parse(server.Requests.Single(request =>
            request.Method == "POST" && request.Path.EndsWith("/objects", StringComparison.Ordinal)
            && !request.Path.Contains("/lists/", StringComparison.Ordinal)).Body)!;
        Assert.DoesNotContain("collections", create.Select(pair => pair.Key));
    }

    [Fact]
    public async Task UpdateCanOnlyChangeCollectionMembershipWithoutPatchingProperties()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Collection("floor-add", "Niveau ajouté"),
            FakeHomeAnytypeServer.Collection("floor-remove", "Niveau retiré"));

        await Gestures(server).UpdateAsync(
            [new HomeUpdateItem(
                "ZZ",
                null,
                null,
                AddToCollections: ["Niveau ajouté"],
                RemoveFromCollections: ["Niveau retiré"])],
            Ct);

        Assert.Contains(server.Requests, request =>
            request.Method == "POST"
            && request.Path.EndsWith("/lists/floor-add/objects", StringComparison.Ordinal));
        Assert.Contains(server.Requests, request =>
            request.Method == "DELETE"
            && request.Path.EndsWith("/lists/floor-remove/objects/room-zz", StringComparison.Ordinal));
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task CollectionMembershipRejectsAnOrdinaryObjectBeforeWriting()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("ZZ", null, null, AddToCollections: ["ZZ"])], Ct));

        Assert.Contains("n’est pas une collection", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method is "POST" or "PATCH" or "DELETE");
    }

    [Fact]
    public async Task CreateRejectsAValidCodeWhoseRoomIsAbsent()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Outlet,
                [new HomeCreateItem("YY-P01", null, null)], Ct));

        Assert.Contains("Code de pièce inconnu", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task CreateRoomKeepsItsCodeInTheTitleAndUpdatePreservesIt()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Room,
            [new HomeCreateItem("ZZ", "Pièce fictive", null)],
            Ct);

        JsonObject create = (JsonObject)JsonNode.Parse(
            server.Requests.Single(request => request.Method == "POST").Body)!;
        Assert.Equal("ZZ — Pièce fictive", create["name"]!.GetValue<string>());

        await Gestures(server).UpdateAsync(
            [new HomeUpdateItem("ZZ", "Pièce renommée", null)],
            Ct);

        JsonObject update = (JsonObject)JsonNode.Parse(
            server.Requests.Single(request => request.Method == "PATCH").Body)!;
        Assert.Equal("ZZ — Pièce renommée", update["name"]!.GetValue<string>());
    }

    [Fact]
    public void ManagedSchemaUsesTheAppliedCircuitAndBoardKeys()
    {
        Assert.Equal("circuit_elec", HomeSchema.Types.Circuit);
        Assert.Equal("tableau_elec", HomeSchema.Types.DistributionBoard);
        Assert.DoesNotContain(
            ((JsonArray)HomeSchema.CreateRequiredSchemaManifest()["properties"]!).OfType<JsonObject>(),
            property => property["key"]?.GetValue<string>() == "code");
    }

    [Fact]
    public async Task DuplicateElementCodeSuggestsTheNextFreeSequence()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Element("element-1", "ZZ-P01", "room-zz"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Outlet,
                [new HomeCreateItem("ZZ-P01", null, null)], Ct));

        Assert.Contains("ZZ-P02", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task UpdateCannotRewriteCodeOrItsDerivedRoomAndCategory()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Element("element-1", "ZZ-P01", "room-zz"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("ZZ-P01", null, new JsonObject { ["code"] = "ZZ-P02" })], Ct));

        Assert.Contains("immuable", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task DeleteRefusesElementsBeforeAnyDeleteRequest()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Element("element-1", "ZZ-P01", "room-zz"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).DeleteAsync("ZZ-P01", confirm: false, Ct));

        Assert.Contains("Existence", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public async Task MissingRequiredSchemaFailsBeforeInventoryReadOrWrite()
    {
        using var server = new FakeHomeAnytypeServer();
        server.RemoveSchemaProperty(HomeSchema.Properties.Room);

        HomeSchemaException error = await Assert.ThrowsAsync<HomeSchemaException>(() =>
            Gestures(server).GetAsync("anything", Ct));

        Assert.Contains("propriété manquante piece", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Path.EndsWith("/objects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateShortIdeaBecomesItsWholeTitleWithoutBody()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Idea,
            [new HomeCreateItem(null, null, null, Text: "Tester des LED fictives en corniche")],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Idea, body["type_key"]!.GetValue<string>());
        Assert.Equal("Tester des LED fictives en corniche", body["name"]!.GetValue<string>());
        Assert.DoesNotContain("body", body.Select(pair => pair.Key));
    }

    [Fact]
    public async Task CreateLongIdeaTitlesItselfFromItsFirstWordsAndKeepsTheTextAsBody()
    {
        using var server = new FakeHomeAnytypeServer();

        string text = "Repeindre le volet fictif du bureau avec la peinture restée du chantier de la chambre, après ponçage.\nVérifier la teinte au préalable.";
        await Gestures(server).CreateAsync(
            HomeSchema.Types.Idea,
            [new HomeCreateItem(null, null, null, Text: text)],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(text, body["body"]!.GetValue<string>());
        string title = body["name"]!.GetValue<string>();
        Assert.StartsWith("Repeindre le volet fictif", title);
        Assert.True(title.Length <= 81, $"title too long: {title.Length}");
    }

    [Fact]
    public async Task LifeTypesRefuseACodeAndAnIdeaRefusesAName()
    {
        using var server = new FakeHomeAnytypeServer();

        InvalidOperationException codeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Errand,
                [new HomeCreateItem("ZZ-P01", "Vis fictives", null)], Ct));
        Assert.Contains("ne porte pas de code", codeError.Message);

        InvalidOperationException nameError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Idea,
                [new HomeCreateItem(null, "Un titre", null, Text: "Une idée fictive")], Ct));
        Assert.Contains("première ligne", nameError.Message);

        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task CreateErrandTakesAFreeNameAndTheGuardedAisle()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Errand,
            [new HomeCreateItem(null, "Vis fictives 4×40", new JsonObject
            {
                ["Rayon"] = "Bricolage",
                ["Quantité"] = "1 boîte de 100",
            })],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal("Vis fictives 4×40", body["name"]!.GetValue<string>());
        JsonArray properties = Assert.IsType<JsonArray>(body["properties"]);
        Assert.Equal("tag-rayon-bricolage", Entry(properties, HomeSchema.Properties.Aisle)["select"]!.GetValue<string>());
        Assert.Equal("1 boîte de 100", Entry(properties, HomeSchema.Properties.Quantity)["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateToolAcceptsAnInitialBodyAndTheSupplierVocabulary()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Tool,
            [new HomeCreateItem(null, "Visseuse fictive", new JsonObject { ["Fournisseur"] = "Occasion" },
                Text: "Achetée pour le chantier fictif.")],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal("Visseuse fictive", body["name"]!.GetValue<string>());
        Assert.Equal("Achetée pour le chantier fictif.", body["body"]!.GetValue<string>());
        Assert.Equal(
            "tag-fournisseur-occasion",
            Entry(Assert.IsType<JsonArray>(body["properties"]), HomeSchema.Properties.Supplier)["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task FilesPropertiesAreRefusedWithAppGuidance()
    {
        using var server = new FakeHomeAnytypeServer();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Tool,
                [new HomeCreateItem(null, "Visseuse fictive", new JsonObject { ["Facture"] = "facture.pdf" })], Ct));

        Assert.Contains("app Anytype", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task IdeaRenameIsRefusedAndErrandRenameReplacesTheWholeTitle()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Idea("idea-1", "Tester une idée fictive"),
            FakeHomeAnytypeServer.Errand("errand-1", "Vis fictives", done: false));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("Tester une idée fictive", "Nouveau titre", null)], Ct));
        Assert.Contains("première ligne", error.Message);

        await Gestures(server).UpdateAsync(
            [new HomeUpdateItem("Vis fictives", "Vis inox fictives", null)], Ct);

        JsonObject update = (JsonObject)JsonNode.Parse(
            server.Requests.Single(request => request.Method == "PATCH").Body)!;
        Assert.Equal("Vis inox fictives", update["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task SearchDoneFilterSplitsCheckedFromUncheckedErrands()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Errand("errand-1", "Vis fictives", done: false),
            FakeHomeAnytypeServer.Errand("errand-2", "Sacs fictifs", done: true));

        string remaining = await Gestures(server).SearchAsync(
            new HomeSearchFilter(null, HomeSchema.Types.Errand, null, null, null, null, null, Done: false), Ct);

        Assert.Contains("Vis fictives", remaining);
        Assert.DoesNotContain("Sacs fictifs", remaining);
    }

    [Fact]
    public void ManagedSchemaCarriesLifeLayoutsAndTheMaterielPlural()
    {
        JsonArray types = (JsonArray)HomeSchema.CreateRequiredSchemaManifest()["types"]!;
        JsonObject TypeSpec(string key) =>
            types.OfType<JsonObject>().Single(value => value["key"]!.GetValue<string>() == key);

        Assert.Equal("note", TypeSpec(HomeSchema.Types.Idea)["layout"]!.GetValue<string>());
        Assert.Equal("action", TypeSpec(HomeSchema.Types.Errand)["layout"]!.GetValue<string>());
        Assert.Equal("basic", TypeSpec(HomeSchema.Types.Tool)["layout"]!.GetValue<string>());
        Assert.Equal("Matériel", TypeSpec(HomeSchema.Types.Tool)["plural_name"]!.GetValue<string>());
        Assert.Equal("basic", TypeSpec(HomeSchema.Types.Room)["layout"]!.GetValue<string>());
    }

    [Fact]
    public async Task WorksiteCreateTakesANameAloneAndTaskCreateAttachesToIt()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateWorksiteAsync("Salle de bain fictive", null, null, Ct);

        JsonObject worksite = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Worksite, worksite["type_key"]!.GetValue<string>());
        Assert.Equal("Salle de bain fictive", worksite["name"]!.GetValue<string>());
        Assert.DoesNotContain("properties", worksite.Select(pair => pair.Key));

        await Gestures(server).CreateTaskAsync("Déposer le lavabo fictif", "Salle de bain fictive", null, Ct);

        JsonObject task = (JsonObject)JsonNode.Parse(server.Requests.Last(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Task, task["type_key"]!.GetValue<string>());
        JsonArray reference = Assert.IsType<JsonArray>(
            Entry(Assert.IsType<JsonArray>(task["properties"]), HomeSchema.Properties.Worksite)["objects"]);
        Assert.Equal("created-1", Assert.Single(reference)!.GetValue<string>());
    }

    [Fact]
    public async Task TaskCreateAllowsAnOrphanAndWorkTypesRefuseCodeAndBody()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateTaskAsync("Purger le radiateur fictif", null, null, Ct);

        JsonObject task = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Task, task["type_key"]!.GetValue<string>());
        Assert.DoesNotContain("properties", task.Select(pair => pair.Key));

        InvalidOperationException codeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Worksite,
                [new HomeCreateItem("ZZ-P01", "Chantier fictif", null)], Ct));
        Assert.Contains("ne porte pas de code", codeError.Message);

        InvalidOperationException bodyError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Task,
                [new HomeCreateItem(null, "Tâche fictive", null, Text: "un corps")], Ct));
        Assert.Contains("Notes", bodyError.Message);
    }

    [Fact]
    public async Task CompleteChecksTheNativeDoneBoxOfATask()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.WorkTask("task-1", "Purger le radiateur fictif", null, done: false));

        string digest = await Gestures(server).CompleteAsync("Purger le radiateur fictif", Ct);

        Assert.Contains("Terminé", digest);
        JsonObject patch = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "PATCH").Body)!;
        JsonObject entry = Entry(Assert.IsType<JsonArray>(patch["properties"]), "done");
        Assert.True(entry["checkbox"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CompleteClosesAWorksiteWithStatutTermineAndCountsOpenTasks()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("site-1", "Salle de bain fictive"),
            FakeHomeAnytypeServer.WorkTask("task-1", "Déposer le lavabo fictif", "site-1", done: false),
            FakeHomeAnytypeServer.WorkTask("task-2", "Choisir le carrelage fictif", "site-1", done: true));

        string digest = await Gestures(server).CompleteAsync("Salle de bain fictive", Ct);

        Assert.Contains("Chantier terminé", digest);
        Assert.Contains("1 tâche", digest);
        JsonObject patch = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "PATCH").Body)!;
        Assert.Equal(
            "tag-statut-termine",
            Entry(Assert.IsType<JsonArray>(patch["properties"]), HomeSchema.Properties.Status)["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task CompleteRefusesAnObjectThatDoesNotFinish()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CompleteAsync("ZZ", Ct));

        Assert.Contains("ne se termine pas", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task WorksiteOverviewGroupsItsTasksByDoneState()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("site-1", "Salle de bain fictive"),
            FakeHomeAnytypeServer.WorkTask("task-1", "Déposer le lavabo fictif", "site-1", done: false),
            FakeHomeAnytypeServer.WorkTask("task-2", "Choisir le carrelage fictif", "site-1", done: true),
            FakeHomeAnytypeServer.WorkTask("task-3", "Corvée orpheline fictive", null, done: false));

        string overview = await Gestures(server).WorksiteOverviewAsync("Salle de bain fictive", Ct);

        Assert.Contains("Tâches ouvertes (1)", overview);
        Assert.Contains("Déposer le lavabo fictif", overview);
        Assert.Contains("Tâches terminées (1)", overview);
        Assert.Contains("Choisir le carrelage fictif", overview);
        Assert.DoesNotContain("Corvée orpheline fictive", overview);
    }

    [Fact]
    public async Task SearchFiltersByWorksiteAndStatut()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("site-1", "Salle de bain fictive"),
            FakeHomeAnytypeServer.Worksite("site-2", "Terrasse fictive"),
            FakeHomeAnytypeServer.WorkTask("task-1", "Déposer le lavabo fictif", "site-1", done: false),
            FakeHomeAnytypeServer.WorkTask("task-2", "Corvée orpheline fictive", null, done: false));

        string byWorksite = await Gestures(server).SearchAsync(
            new HomeSearchFilter(null, null, null, null, null, null, null, Worksite: "Salle de bain fictive"), Ct);
        Assert.Contains("Déposer le lavabo fictif", byWorksite);
        Assert.DoesNotContain("Corvée orpheline fictive", byWorksite);

        string byStatut = await Gestures(server).SearchAsync(
            new HomeSearchFilter(null, HomeSchema.Types.Worksite, null, null, null, null, null, Status: "En cours"), Ct);
        Assert.Contains("Salle de bain fictive", byStatut);
    }

    [Fact]
    public async Task DeleteIgnoresAnytypeLinkGraphButKeepsDomainReferences()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("site-1", "Salle de bain fictive", "task-1"),
            FakeHomeAnytypeServer.WorkTask("task-1", "Déposer le lavabo fictif", "site-1", done: true));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).DeleteAsync("site-1", confirm: true, Ct));
        Assert.Contains("encore référencé", error.Message);

        string digest = await Gestures(server).DeleteAsync("task-1", confirm: true, Ct);
        Assert.Contains("corbeille", digest);
    }

    [Fact]
    public void ManagedSchemaCarriesTheWorkTypesWithTheirLayouts()
    {
        JsonArray types = (JsonArray)HomeSchema.CreateRequiredSchemaManifest()["types"]!;
        JsonObject TypeSpec(string key) =>
            types.OfType<JsonObject>().Single(value => value["key"]!.GetValue<string>() == key);

        Assert.Equal("basic", TypeSpec(HomeSchema.Types.Worksite)["layout"]!.GetValue<string>());
        Assert.Equal("action", TypeSpec(HomeSchema.Types.Task)["layout"]!.GetValue<string>());
        Assert.Equal("Chantiers", TypeSpec(HomeSchema.Types.Worksite)["plural_name"]!.GetValue<string>());
    }

    private static JsonObject Entry(JsonArray properties, string key) =>
        properties.OfType<JsonObject>().Single(value => value["key"]!.GetValue<string>() == key);
}
