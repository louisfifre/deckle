using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// The Home gestures read whole listings. These tests serve them through a
// server that caps a page below the limit the caller asks for, the way a
// listing may answer, so a caller that advances by the limit instead of by the
// rows it received skips rows and fails here.
[Trait("Category", "regression")]
public class HomePagingTests
{
    private const int Fillers = 240;
    private const int PageCap = 100;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    private static IEnumerable<JsonObject> Plants(int start, int count) =>
        Enumerable.Range(start, count).Select(i => FakeHomeAnytypeServer.Plant($"plant-{i}", $"Plante fictive {i}"));

    [Fact]
    public async Task DeleteSeesAReferenceHeldBeyondTheFirstPageOfTheInventory()
    {
        using var server = new FakeHomeAnytypeServer { PageCap = PageCap };
        // The referencing control sits at index 150: past the first page, and
        // inside the range a stride of 1000 would skip.
        var objects = new List<JsonObject>
        {
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-LC01", "Plafonnier fictif", "room-zz"),
        };
        objects.AddRange(Plants(0, 148));
        objects.Add(FakeHomeAnytypeServer.Point("point-2", "ZZ-CS01", "Interrupteur fictif", "room-zz", "point-1"));
        objects.AddRange(Plants(148, Fillers - 148));
        server.SetObjects([.. objects]);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).DeleteAsync("ZZ-LC01", confirm: false, Ct));

        Assert.Contains("référencé", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public async Task CreateRefusesACodeAlreadyHeldBeyondTheFirstPageOfTheInventory()
    {
        using var server = new FakeHomeAnytypeServer { PageCap = PageCap };
        var objects = new List<JsonObject>(Plants(0, 150))
        {
            FakeHomeAnytypeServer.Circuit("circuit-1", "B1.7"),
        };
        objects.AddRange(Plants(150, Fillers - 150));
        server.SetObjects([.. objects]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Circuit,
                [new HomeCreateItem("B1.7", null, null)], Ct));

        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task InventoryReadsEveryPageEvenWhenTheServerServesFewerRowsThanAsked()
    {
        using var server = new FakeHomeAnytypeServer { PageCap = PageCap };
        server.SetObjects([.. Plants(0, Fillers)]);

        // A create against a clean inventory reads it whole; the number of
        // listing requests shows the loop advanced one served page at a time.
        await Gestures(server).CreateAsync(
            HomeSchema.Types.Circuit, [new HomeCreateItem("B1.8", null, null)], Ct);

        string[] listings = server.Requests
            .Where(request => request.Method == "GET" && request.Path.EndsWith("/objects", StringComparison.Ordinal))
            .Select(request => request.Path)
            .ToArray();
        Assert.True(listings.Length >= (Fillers + PageCap - 1) / PageCap);
    }

    [Fact]
    public async Task TemplateLookupFindsATemplateBeyondTheFirstServedPage()
    {
        using var server = new FakeHomeAnytypeServer { PageCap = 40 };
        for (int i = 0; i < 120; i++)
            server.AddTemplate(HomeSchema.Types.Device, $"template-{i}", $"Modèle fictif {i}");

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device,
            [new HomeCreateItem(null, "Appareil fictif", null, Template: "Modèle fictif 90")],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request =>
            request.Method == "POST").Body)!;
        Assert.Equal("template-90", body["template_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task TemplateLookupEndsWhenTheServerKeepsAnnouncingPagesItNeverServes()
    {
        using var server = new FakeHomeAnytypeServer { StuckTemplates = true };

        // Without a guard on the empty page the loop never returns; the
        // timeout turns that hang into a failure instead of a stuck run.
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Device,
                [new HomeCreateItem(null, "Appareil fictif", null, Template: "Modèle fictif")],
                Ct).WaitAsync(TimeSpan.FromSeconds(20), Ct));

        Assert.Contains("Modèle inconnu", error.Message);
    }

    [Fact]
    public async Task OptionLookupFindsALiveOptionBeyondTheFirstServedPage()
    {
        using var server = new FakeHomeAnytypeServer { PageCap = 40 };
        for (int i = 0; i < 120; i++)
            server.AddSchemaTag(HomeSchema.Properties.Supplier, $"fournisseur-{i}", $"Fournisseur fictif {i}");

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device,
            [new HomeCreateItem(null, "Visseuse fictive", new JsonObject { ["Fournisseur"] = "Fournisseur fictif 90" })],
            Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(server.Requests.Single(request =>
            request.Method == "POST" && request.Path.EndsWith("/objects", StringComparison.Ordinal)).Body)!;
        JsonObject supplier = ((JsonArray)body["properties"]!).OfType<JsonObject>()
            .Single(entry => entry["key"]!.GetValue<string>() == HomeSchema.Properties.Supplier);
        Assert.Equal("tag-" + HomeSchema.Properties.Supplier + "-fournisseur-90", supplier["select"]!.GetValue<string>());
    }}
