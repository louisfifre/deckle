using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// The errands: need puts a product on the list, taken takes it off, list
// reads it. A product is found by its name and never guessed between two
// close names.
[Trait("Category", "integration")]
public sealed class HomeErrandsTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    static JsonObject Entry(JsonNode body, string key) =>
        body["properties"]!.AsArray().OfType<JsonObject>().Single(entry => entry["key"]!.GetValue<string>() == key);

    static bool IsWrite(FakeHomeAnytypeServer.Received request) => request.Method is "POST" or "PATCH";

    [Fact]
    public async Task NeedSetsTheProductWhoseNameMatchesCaseAndAccentsAside()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.ShoppingProduct("product-1", "Tomates fictives", needed: false));

        string digest = await Gestures(server).ProductNeedAsync(
            [new HomeProductNeed("tomates FICTIVES", "Légumes", 3)], Ct);

        Assert.Equal("besoin · Tomates fictives (existant)", digest);
        FakeHomeAnytypeServer.Received patch = Assert.Single(server.Requests, IsWrite);
        Assert.Equal("PATCH", patch.Method);
        Assert.EndsWith("/objects/product-1", patch.Path);
        JsonNode body = JsonNode.Parse(patch.Body)!;
        Assert.True(Entry(body, HomeSchema.Properties.Needed)["checkbox"]!.GetValue<bool>());
        Assert.Equal("tag-product_category-vegetables", Entry(body, HomeSchema.Properties.ProductCategory)["select"]!.GetValue<string>());
        Assert.Equal(3, Entry(body, HomeSchema.Properties.Quantity)["number"]!.GetValue<double>());
    }

    [Fact]
    public async Task NeedRefusesANearNameWithItsCandidatesAndWritesNothing()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.ShoppingProduct("product-1", "Tomates fictives", needed: false));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).ProductNeedAsync(
                [new HomeProductNeed("Pain fictif"), new HomeProductNeed("Tomate fictive")], Ct));

        Assert.Contains("- product-1 · Tomates fictives (product)", error.Message);
        Assert.Contains("force: true", error.Message);
        Assert.DoesNotContain(server.Requests, IsWrite);
    }

    [Fact]
    public async Task NeedWithForceCreatesADifferentProductDespiteANearName()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.ShoppingProduct("product-1", "Tomates fictives", needed: false));

        string digest = await Gestures(server).ProductNeedAsync(
            [new HomeProductNeed("Tomate fictive", Force: true)], Ct);

        Assert.Equal("besoin · Tomate fictive (créé)", digest);
        Assert.Equal("POST", Assert.Single(server.Requests, IsWrite).Method);
    }

    [Fact]
    public async Task NeedCreatesAnUnknownProductAlreadyNeeded()
    {
        using var server = new FakeHomeAnytypeServer();

        string digest = await Gestures(server).ProductNeedAsync([new HomeProductNeed("Pain fictif")], Ct);

        Assert.Equal("besoin · Pain fictif (créé)", digest);
        JsonNode body = JsonNode.Parse(Assert.Single(server.Requests, IsWrite).Body)!;
        Assert.Equal(HomeSchema.Types.Product, body["type_key"]!.GetValue<string>());
        Assert.Equal("Pain fictif", body["name"]!.GetValue<string>());
        Assert.True(Entry(body, HomeSchema.Properties.Needed)["checkbox"]!.GetValue<bool>());
    }

    [Fact]
    public async Task AProductNeededAgainComesBackFromTheArchive()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.ShoppingProduct("product-1", "Pain fictif", needed: false, archived: true));

        await Gestures(server).ProductNeedAsync([new HomeProductNeed("Pain fictif")], Ct);

        JsonNode body = JsonNode.Parse(Assert.Single(server.Requests, IsWrite).Body)!;
        Assert.False(Entry(body, HomeSchema.Properties.Archived)["checkbox"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TakenUnsetsNeededOnTheExactProduct()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.ShoppingProduct("product-1", "Pain fictif", needed: true));

        string digest = await Gestures(server).ProductTakenAsync(["pain fictif"], Ct);

        Assert.Equal("pris · Pain fictif (existant)", digest);
        JsonNode body = JsonNode.Parse(Assert.Single(server.Requests, IsWrite).Body)!;
        Assert.False(Entry(body, HomeSchema.Properties.Needed)["checkbox"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TakenRefusesANearNameAndPointsToProductCreateForADifferentOne()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.ShoppingProduct("product-1", "Pains fictifs", needed: true));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).ProductTakenAsync(["Pain fictif"], Ct));

        Assert.Contains("product-1", error.Message);
        Assert.Contains("product_create", error.Message);
        Assert.DoesNotContain(server.Requests, IsWrite);
    }

    [Fact]
    public async Task TakenCreatesAnUnknownProductAlreadyTaken()
    {
        using var server = new FakeHomeAnytypeServer();

        string digest = await Gestures(server).ProductTakenAsync(["Confiture fictive"], Ct);

        Assert.Equal("pris · Confiture fictive (créé)", digest);
        JsonNode body = JsonNode.Parse(Assert.Single(server.Requests, IsWrite).Body)!;
        Assert.Equal("POST", Assert.Single(server.Requests, IsWrite).Method);
        Assert.False(Entry(body, HomeSchema.Properties.Needed)["checkbox"]!.GetValue<bool>());
    }

    static FakeHomeAnytypeServer ServerWithAList()
    {
        var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.ShoppingProduct("p-clean", "Savon fictif", needed: true, "cleaning"),
            FakeHomeAnytypeServer.ShoppingProduct("p-veg", "Carottes fictives", needed: true, "vegetables", quantity: 2),
            FakeHomeAnytypeServer.ShoppingProduct("p-none", "Objet fictif", needed: true),
            FakeHomeAnytypeServer.ShoppingProduct("p-sweet", "Chocolat fictif", needed: true, "sweet_products"),
            FakeHomeAnytypeServer.ShoppingProduct("p-done", "Sel fictif", needed: false, "seasonings"),
            FakeHomeAnytypeServer.ShoppingProduct("p-archived", "Lait fictif", needed: true, "dairy_products", archived: true));
        return server;
    }

    [Fact]
    public async Task ListGroupsInTheOptionOrderWithTheUncategorisedLast()
    {
        using FakeHomeAnytypeServer server = ServerWithAList();

        HomeProductList list = await Gestures(server).ProductListAsync(ct: Ct);

        JsonArray groups = list.Data["groups"]!.AsArray();
        Assert.Equal(
            new string?[] { "sweet_products", "vegetables", "cleaning", null },
            groups.Select(group => group!["category"]?.GetValue<string>()));
        Assert.Equal(4, list.Data["total"]!.GetValue<int>());
        Assert.Equal(2, groups[1]!["items"]![0]!["quantity"]!.GetValue<double>());
        Assert.DoesNotContain("Sel fictif", list.Text);
        Assert.DoesNotContain("Lait fictif", list.Text);
        Assert.True(list.Text.IndexOf("Produits sucrés", StringComparison.Ordinal)
            < list.Text.IndexOf("Légumes", StringComparison.Ordinal));
        Assert.EndsWith("Sans catégorie :\n- Objet fictif", list.Text);
        Assert.Contains("Carottes fictives (quantité 2)", list.Text);
    }

    [Fact]
    public async Task ListKeepsOneCategoryNamedByItsLabel()
    {
        using FakeHomeAnytypeServer server = ServerWithAList();

        HomeProductList list = await Gestures(server).ProductListAsync("Légumes", Ct);

        JsonObject group = Assert.IsType<JsonObject>(Assert.Single(list.Data["groups"]!.AsArray()));
        Assert.Equal("p-veg", group["items"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task ListRefusesAnUnknownCategoryAndNamesTheKnownOnes()
    {
        using FakeHomeAnytypeServer server = ServerWithAList();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).ProductListAsync("Catégorie fictive", Ct));

        Assert.Contains("Légumes", error.Message);
    }

    [Fact]
    public async Task TheListToolReturnsItsGroupsAsStructuredContent()
    {
        using FakeHomeAnytypeServer server = ServerWithAList();
        ToolDescriptor tool = HomeToolCatalog.Build(() => Gestures(server)).Single(value => value.Name == "product_list");

        ToolOutput output = await tool.Handler(new JsonObject(), Ct);

        Assert.NotNull(tool.OutputSchema);
        Assert.Equal(4, output.StructuredContent!["groups"]!.AsArray().Count);
    }
}
