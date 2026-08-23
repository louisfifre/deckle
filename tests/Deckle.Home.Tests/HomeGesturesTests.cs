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
    public async Task CreatePointDerivesRoomCategoryExistenceAndStoresTheCodeProperty()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        string digest = await Gestures(server).CreateAsync(
            HomeSchema.Types.Point,
            [new HomeCreateItem("ZZ-P01", "Prise témoin", null)],
            Ct);

        Assert.Contains("ZZ-P01", digest);
        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Point, body["type_key"]!.GetValue<string>());
        Assert.Equal("Prise témoin", body["name"]!.GetValue<string>());
        JsonArray properties = Assert.IsType<JsonArray>(body["properties"]);
        Assert.Equal("ZZ-P01", Entry(properties, HomeSchema.Properties.Code)["text"]!.GetValue<string>());
        JsonNode roomReference = Assert.Single(Assert.IsType<JsonArray>(
            Entry(properties, HomeSchema.Properties.InstalledIn)["objects"]))!;
        Assert.Equal("room-zz", roomReference.GetValue<string>());
        Assert.Equal("tag-category-p", Entry(properties, HomeSchema.Properties.Category)["select"]!.GetValue<string>());
        Assert.Equal("tag-existence-existant", Entry(properties, HomeSchema.Properties.Existence)["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreatePointRequiresAHumanName()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Point,
                [new HomeCreateItem("ZZ-P01", null, null)], Ct));

        Assert.Contains("nom humain", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task CreateCircuitFallsBackToItsCodeAsProvisionalTitle()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Circuit,
            [new HomeCreateItem("B1.7", null, null)],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal("B1.7", body["name"]!.GetValue<string>());
        Assert.Equal(
            "B1.7",
            Entry(Assert.IsType<JsonArray>(body["properties"]), HomeSchema.Properties.Code)["text"]!.GetValue<string>());
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
                HomeSchema.Types.Point,
                [new HomeCreateItem("YY-P01", "Prise fictive", null)], Ct));

        Assert.Contains("Code de pièce inconnu", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task CreateRoomTakesAHumanTitleAndUpdateRenamesItPlainly()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Room,
            [new HomeCreateItem("ZZ", "Pièce fictive", null)],
            Ct);

        JsonObject create = (JsonObject)JsonNode.Parse(
            server.Requests.Single(request => request.Method == "POST").Body)!;
        Assert.Equal("Pièce fictive", create["name"]!.GetValue<string>());
        Assert.Equal(
            "ZZ",
            Entry(Assert.IsType<JsonArray>(create["properties"]), HomeSchema.Properties.Code)["text"]!.GetValue<string>());

        await Gestures(server).UpdateAsync(
            [new HomeUpdateItem("ZZ", "Pièce renommée", null)],
            Ct);

        JsonObject update = (JsonObject)JsonNode.Parse(
            server.Requests.Single(request => request.Method == "PATCH").Body)!;
        Assert.Equal("Pièce renommée", update["name"]!.GetValue<string>());
        Assert.DoesNotContain("properties", update.Select(pair => pair.Key));
    }

    [Fact]
    public void ManagedSchemaCarriesTheNewKeysAndTheCodeProperty()
    {
        Assert.Equal("circuit", HomeSchema.Types.Circuit);
        Assert.Equal("panel", HomeSchema.Types.Panel);
        Assert.Equal("point", HomeSchema.Types.Point);
        JsonArray properties = (JsonArray)HomeSchema.CreateRequiredSchemaManifest()["properties"]!;
        Assert.Contains(
            properties.OfType<JsonObject>(),
            property => property["key"]?.GetValue<string>() == "code");

        JsonObject domain = properties.OfType<JsonObject>()
            .Single(property => property["key"]?.GetValue<string>() == "domain");
        Assert.Contains(
            ((JsonArray)domain["tags"]!).OfType<JsonObject>(),
            tag => tag["key"]?.GetValue<string>() == "electronique");

        JsonObject supplier = properties.OfType<JsonObject>()
            .Single(property => property["key"]?.GetValue<string>() == "supplier");
        Assert.Null(supplier["tags"]);
    }

    [Fact]
    public async Task DuplicatePointCodeSuggestsTheNextFreeSequence()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-P01", "Prise fictive", "room-zz"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Point,
                [new HomeCreateItem("ZZ-P01", "Prise doublon", null)], Ct));

        Assert.Contains("ZZ-P02", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task UpdateCannotRewriteCodeOrItsDerivedRoomAndCategory()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-P01", "Prise fictive", "room-zz"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("ZZ-P01", null, new JsonObject { ["code"] = "ZZ-P02" })], Ct));

        Assert.Contains("immuable", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task DeleteRefusesAReferencedPointBeforeAnyDeleteRequest()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-L01", "Plafonnier fictif", "room-zz"),
            FakeHomeAnytypeServer.Point("point-2", "ZZ-C01", "Interrupteur fictif", "room-zz", "point-1"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).DeleteAsync("ZZ-L01", confirm: false, Ct));

        Assert.Contains("Existence", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public async Task DeleteRetractsAnUnreferencedPointEntryMistake()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-P01", "Prise saisie par erreur", "room-zz"));

        string preview = await Gestures(server).DeleteAsync("ZZ-P01", confirm: false, Ct);
        Assert.Contains("point-1", preview);

        string result = await Gestures(server).DeleteAsync("point-1", confirm: true, Ct);
        Assert.Contains("corbeille", result);
        Assert.Contains(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public async Task MissingRequiredSchemaFailsBeforeInventoryReadOrWrite()
    {
        using var server = new FakeHomeAnytypeServer();
        server.RemoveSchemaProperty(HomeSchema.Properties.InstalledIn);

        HomeSchemaException error = await Assert.ThrowsAsync<HomeSchemaException>(() =>
            Gestures(server).GetAsync("anything", Ct));

        Assert.Contains("propriété manquante installed_in", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Path.EndsWith("/objects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComponentCreationRequiresAnExistingSystem()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.EquipmentSystem("system-1", "PC fictif"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Component,
                [new HomeCreateItem(null, "GPU fictif", null)], Ct));
        Assert.Contains("son système", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");

        await Gestures(server).CreateComponentAsync("GPU fictif", "PC fictif", null, Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Component, body["type_key"]!.GetValue<string>());
        JsonNode reference = Assert.Single(Assert.IsType<JsonArray>(
            Entry(Assert.IsType<JsonArray>(body["properties"]), HomeSchema.Properties.PartOf)["objects"]))!;
        Assert.Equal("system-1", reference.GetValue<string>());
    }

    [Fact]
    public async Task ComponentCannotBeOrphanedByUpdate()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.EquipmentSystem("system-1", "PC fictif"),
            FakeHomeAnytypeServer.Component("component-1", "GPU fictif", "system-1"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("GPU fictif", null, new JsonObject
                {
                    ["Fait partie de"] = new JsonArray(),
                })], Ct));

        Assert.Contains("orpheline", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task PartOfOnlyTargetsASystem()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.EquipmentSystem("system-1", "HiFi fictive"),
            FakeHomeAnytypeServer.Plant("plant-1", "Ficus fictif"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateComponentAsync("Ampli fictif", "Ficus fictif", null, Ct));

        Assert.Contains("Aucun objet Home trouvé", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task PlantCreateAnchorsTheRoom()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        await Gestures(server).CreatePlantAsync("Ficus fictif", "ZZ", null, Ct);

        JsonObject create = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Plant, create["type_key"]!.GetValue<string>());
        JsonNode room = Assert.Single(Assert.IsType<JsonArray>(
            Entry(Assert.IsType<JsonArray>(create["properties"]), HomeSchema.Properties.InstalledIn)["objects"]))!;
        Assert.Equal("room-zz", room.GetValue<string>());
    }

    [Fact]
    public async Task FloorRelationGuidesTowardTheAppWhileTheFloorTypeIsAbsent()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Collection("floor-1", "Rez fictif"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("ZZ", null, new JsonObject { ["Zone"] = "Rez fictif" })], Ct));

        Assert.Contains("layout Collection", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task FloorRelationResolvesTheFloorTypedCollectionOnceDiscovered()
    {
        using var server = new FakeHomeAnytypeServer();
        server.AddSchemaType(HomeSchema.Types.Floor, "Zone", "Zones", "collection");
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Collection("floor-1", "Rez fictif", HomeSchema.Types.Floor),
            FakeHomeAnytypeServer.Collection("section-1", "Section fictive"));

        InvalidOperationException wrongCollection = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("ZZ", null, new JsonObject { ["Zone"] = "Section fictive" })], Ct));
        Assert.Contains("n'est pas une Zone", wrongCollection.Message);

        await Gestures(server).UpdateAsync(
            [new HomeUpdateItem("ZZ", null, new JsonObject { ["Zone"] = "Rez fictif" })], Ct);

        JsonObject patch = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "PATCH").Body)!;
        JsonNode floor = Assert.Single(Assert.IsType<JsonArray>(
            Entry(Assert.IsType<JsonArray>(patch["properties"]), HomeSchema.Properties.Floor)["objects"]))!;
        Assert.Equal("floor-1", floor.GetValue<string>());
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
    public async Task FreeTitledTypesRefuseACodeAndAnIdeaRefusesAName()
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
    public async Task CreateErrandTakesAFreeNameTheGuardedAisleAndANumericQuantity()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Errand,
            [new HomeCreateItem(null, "Vis fictives 4×40 (boîte de 100)", new JsonObject
            {
                ["Rayon"] = "Bricolage",
                ["Quantité"] = 2,
            })],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal("Vis fictives 4×40 (boîte de 100)", body["name"]!.GetValue<string>());
        JsonArray properties = Assert.IsType<JsonArray>(body["properties"]);
        Assert.Equal("tag-aisle-bricolage", Entry(properties, HomeSchema.Properties.Aisle)["select"]!.GetValue<string>());
        Assert.Equal(2, Entry(properties, HomeSchema.Properties.Quantity)["number"]!.GetValue<double>());
    }

    [Fact]
    public async Task CreateDeviceAcceptsAnInitialBodyAndALiveSupplierOption()
    {
        // Supplier is an open vocabulary since the 2026-08-12 reboot: its
        // options live in the space, not in the compiled schema.
        using var server = new FakeHomeAnytypeServer();
        server.AddSchemaTag(HomeSchema.Properties.Supplier, "occasion", "Occasion");

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device,
            [new HomeCreateItem(null, "Visseuse fictive", new JsonObject { ["Fournisseur"] = "Occasion" },
                Text: "Achetée pour le chantier fictif.")],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal("Visseuse fictive", body["name"]!.GetValue<string>());
        Assert.Equal("Achetée pour le chantier fictif.", body["body"]!.GetValue<string>());
        Assert.Equal(
            "tag-supplier-occasion",
            Entry(Assert.IsType<JsonArray>(body["properties"]), HomeSchema.Properties.Supplier)["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task FilesPropertiesAreRefusedWithAppGuidance()
    {
        using var server = new FakeHomeAnytypeServer();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Device,
                [new HomeCreateItem(null, "Visseuse fictive", new JsonObject { ["Preuve d'achat"] = "facture.pdf" })], Ct));

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
    public async Task SearchSystemFilterKeepsTheAggregateMembers()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.EquipmentSystem("system-1", "PC fictif"),
            FakeHomeAnytypeServer.Component("component-1", "GPU fictif", "system-1"),
            FakeHomeAnytypeServer.Plant("plant-1", "Ficus fictif"));

        string members = await Gestures(server).SearchAsync(
            new HomeSearchFilter(null, null, null, null, null, null, null, System: "PC fictif"), Ct);

        Assert.Contains("GPU fictif", members);
        Assert.DoesNotContain("Ficus fictif", members);
    }

    [Fact]
    public void ManagedSchemaCarriesTheNewLayoutsAndPlurals()
    {
        JsonArray types = (JsonArray)HomeSchema.CreateRequiredSchemaManifest()["types"]!;
        JsonObject TypeSpec(string key) =>
            types.OfType<JsonObject>().Single(value => value["key"]!.GetValue<string>() == key);

        Assert.Equal("note", TypeSpec(HomeSchema.Types.Idea)["layout"]!.GetValue<string>());
        Assert.Equal("action", TypeSpec(HomeSchema.Types.Errand)["layout"]!.GetValue<string>());
        Assert.Equal("action", TypeSpec(HomeSchema.Types.Todo)["layout"]!.GetValue<string>());
        Assert.Equal("action", TypeSpec(HomeSchema.Types.Worksite)["layout"]!.GetValue<string>());
        Assert.Equal("basic", TypeSpec(HomeSchema.Types.Device)["layout"]!.GetValue<string>());
        Assert.Equal("basic", TypeSpec(HomeSchema.Types.Utensil)["layout"]!.GetValue<string>());
        Assert.Equal("Appareils", TypeSpec(HomeSchema.Types.Device)["plural_name"]!.GetValue<string>());
        Assert.Equal(
            "Ustensiles de cuisine",
            TypeSpec(HomeSchema.Types.Utensil)["plural_name"]!.GetValue<string>());
        Assert.Equal("Chantiers", TypeSpec(HomeSchema.Types.Worksite)["plural_name"]!.GetValue<string>());
        Assert.DoesNotContain(
            types.OfType<JsonObject>(),
            value => value["key"]!.GetValue<string>() == HomeSchema.Types.Floor);
    }

    [Fact]
    public async Task WorksiteCreateTakesANameAloneAndTodoCreateAttachesToIt()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateWorksiteAsync("Salle de bain fictive", null, null, Ct);

        JsonObject worksite = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Worksite, worksite["type_key"]!.GetValue<string>());
        Assert.Equal("Salle de bain fictive", worksite["name"]!.GetValue<string>());
        Assert.DoesNotContain("properties", worksite.Select(pair => pair.Key));

        await Gestures(server).CreateTodoAsync("Déposer le lavabo fictif", "Salle de bain fictive", null, Ct);

        JsonObject todo = (JsonObject)JsonNode.Parse(server.Requests.Last(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Todo, todo["type_key"]!.GetValue<string>());
        JsonArray reference = Assert.IsType<JsonArray>(
            Entry(Assert.IsType<JsonArray>(todo["properties"]), HomeSchema.Properties.Worksite)["objects"]);
        Assert.Equal("created-1", Assert.Single(reference)!.GetValue<string>());
    }

    [Fact]
    public async Task TodoCreateAllowsAnOrphanAndWorkTypesRefuseCodeAndBody()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateTodoAsync("Purger le radiateur fictif", null, null, Ct);

        JsonObject todo = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "POST").Body)!;
        Assert.Equal(HomeSchema.Types.Todo, todo["type_key"]!.GetValue<string>());
        Assert.DoesNotContain("properties", todo.Select(pair => pair.Key));

        InvalidOperationException codeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Worksite,
                [new HomeCreateItem("ZZ-P01", "Chantier fictif", null)], Ct));
        Assert.Contains("ne porte pas de code", codeError.Message);

        InvalidOperationException bodyError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Todo,
                [new HomeCreateItem(null, "Tâche fictive", null, Text: "un corps")], Ct));
        Assert.Contains("Notes", bodyError.Message);
    }

    [Fact]
    public async Task CompleteChecksTheNativeDoneBoxOfATodo()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Todo("todo-1", "Purger le radiateur fictif", null, done: false));

        string digest = await Gestures(server).CompleteAsync("Purger le radiateur fictif", Ct);

        Assert.Contains("Terminé", digest);
        JsonObject patch = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "PATCH").Body)!;
        JsonObject entry = Entry(Assert.IsType<JsonArray>(patch["properties"]), "done");
        Assert.True(entry["checkbox"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CompleteClosesAWorksiteWithStateTermineAndCountsOpenTodos()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("site-1", "Salle de bain fictive"),
            FakeHomeAnytypeServer.Todo("todo-1", "Déposer le lavabo fictif", "site-1", done: false),
            FakeHomeAnytypeServer.Todo("todo-2", "Choisir le carrelage fictif", "site-1", done: true));

        string digest = await Gestures(server).CompleteAsync("Salle de bain fictive", Ct);

        Assert.Contains("Chantier terminé", digest);
        Assert.Contains("1 tâche", digest);
        JsonObject patch = (JsonObject)JsonNode.Parse(server.Requests.Single(r => r.Method == "PATCH").Body)!;
        Assert.Equal(
            "tag-state-termine",
            Entry(Assert.IsType<JsonArray>(patch["properties"]), HomeSchema.Properties.State)["select"]!.GetValue<string>());
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
    public async Task WorksiteOverviewGroupsItsTodosByDoneState()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("site-1", "Salle de bain fictive"),
            FakeHomeAnytypeServer.Todo("todo-1", "Déposer le lavabo fictif", "site-1", done: false),
            FakeHomeAnytypeServer.Todo("todo-2", "Choisir le carrelage fictif", "site-1", done: true),
            FakeHomeAnytypeServer.Todo("todo-3", "Corvée orpheline fictive", null, done: false));

        string overview = await Gestures(server).WorksiteOverviewAsync("Salle de bain fictive", Ct);

        Assert.Contains("Tâches ouvertes (1)", overview);
        Assert.Contains("Déposer le lavabo fictif", overview);
        Assert.Contains("Tâches terminées (1)", overview);
        Assert.Contains("Choisir le carrelage fictif", overview);
        Assert.DoesNotContain("Corvée orpheline fictive", overview);
    }

    [Fact]
    public async Task SearchFiltersByWorksiteAndState()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("site-1", "Salle de bain fictive"),
            FakeHomeAnytypeServer.Worksite("site-2", "Terrasse fictive"),
            FakeHomeAnytypeServer.Todo("todo-1", "Déposer le lavabo fictif", "site-1", done: false),
            FakeHomeAnytypeServer.Todo("todo-2", "Corvée orpheline fictive", null, done: false));

        string byWorksite = await Gestures(server).SearchAsync(
            new HomeSearchFilter(null, null, null, null, null, null, null, Worksite: "Salle de bain fictive"), Ct);
        Assert.Contains("Déposer le lavabo fictif", byWorksite);
        Assert.DoesNotContain("Corvée orpheline fictive", byWorksite);

        string byState = await Gestures(server).SearchAsync(
            new HomeSearchFilter(null, HomeSchema.Types.Worksite, null, null, null, null, null, State: "En cours"), Ct);
        Assert.Contains("Salle de bain fictive", byState);
    }

    [Fact]
    public async Task DeleteIgnoresAnytypeLinkGraphButKeepsDomainReferences()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Worksite("site-1", "Salle de bain fictive", "todo-1"),
            FakeHomeAnytypeServer.Todo("todo-1", "Déposer le lavabo fictif", "site-1", done: true));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).DeleteAsync("site-1", confirm: true, Ct));
        Assert.Contains("encore référencé", error.Message);

        string digest = await Gestures(server).DeleteAsync("todo-1", confirm: true, Ct);
        Assert.Contains("corbeille", digest);
    }

    [Fact]
    public async Task CreateResolvesTheTemplateByNameAndSendsItsIdWithTheBody()
    {
        using var server = new FakeHomeAnytypeServer();
        server.AddTemplate(HomeSchema.Types.Device, "template-fictif", "Téléphone fictif");

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device,
            [new HomeCreateItem(
                null, "Appareil fictif", null, Text: "Corps fictif", Template: "telephone fictif")],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request =>
            request.Method == "POST").Body)!;
        Assert.Equal("template-fictif", body["template_id"]!.GetValue<string>());
        Assert.Equal("Corps fictif", body["body"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateRefusesAnUnknownTemplateAndNamesTheOnesTheTypeHas()
    {
        using var server = new FakeHomeAnytypeServer();
        server.AddTemplate(HomeSchema.Types.Device, "template-fictif", "Téléphone fictif");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Device,
                [new HomeCreateItem(null, "Appareil fictif", null, Template: "Enceinte fictive")],
                Ct));

        Assert.Contains("Modèle inconnu", error.Message);
        Assert.Contains("Téléphone fictif", error.Message);
        Assert.DoesNotContain(server.Requests, request =>
            request.Method == "POST" && request.Path.EndsWith("/objects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateWithoutATemplateSendsNoTemplateIdAndAsksTheTypeForNone()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device,
            [new HomeCreateItem(null, "Appareil fictif", null)],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request =>
            request.Method == "POST").Body)!;
        Assert.DoesNotContain("template_id", body.Select(pair => pair.Key));
        Assert.DoesNotContain(server.Requests, request =>
            request.Path.EndsWith("/templates", StringComparison.Ordinal));
    }

    private static JsonObject Entry(JsonArray properties, string key) =>
        properties.OfType<JsonObject>().Single(value => value["key"]!.GetValue<string>() == key);
}
