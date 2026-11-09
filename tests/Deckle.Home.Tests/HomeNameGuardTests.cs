using System.Diagnostics.Tracing;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Home.Tests;

// Two names are near when they fold to the same form: case, accents,
// ligatures, punctuation, spacing and a plural ending do not count.
[Trait("Category", "unit")]
public sealed class HomeNameFoldTests
{
    [Theory]
    [InlineData("Visseuse fictive", "visseuses  FICTIVES !")]
    [InlineData("Œuf fictif", "oeufs fictifs")]
    [InlineData("Café-crème fictif", "cafe creme fictif")]
    [InlineData("Ampoule fictive E27", "ampoules fictives e27")]
    [InlineData("Vis fictives", "VIS fictive")]
    public void NamesThatOnlyDifferInFormFoldAlike(string first, string second) =>
        Assert.Equal(HomeNameGuard.Fold(first), HomeNameGuard.Fold(second));

    [Theory]
    [InlineData("Lampe fictive 1", "Lampe fictive 2")]
    [InlineData("Vis fictive", "Vit fictive")]
    [InlineData("Vis fictive", "Vi fictive")]
    public void NamesThatDifferInSubstanceStayApart(string first, string second) =>
        Assert.NotEqual(HomeNameGuard.Fold(first), HomeNameGuard.Fold(second));
}

[Trait("Category", "integration")]
public sealed class HomeNameGuardTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    [Fact]
    public async Task ANearNameIsRefusedWithTheObjectItResembles()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Device("device-1", "Visseuse fictive"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Device, [new HomeCreateItem(null, "visseuses FICTIVES", null)], Ct));

        Assert.Contains("Nom trop proche", error.Message);
        Assert.Contains("- device-1 · Visseuse fictive (device)", error.Message);
        Assert.Contains("force: true", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task ForcePassesTheGuardForATrueTwin()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Device("device-1", "Visseuse fictive"));

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device, [new HomeCreateItem(null, "Visseuse fictive", null, Force: true)], Ct);

        Assert.Single(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task TwoItemsOfOneBatchThatFoldAlikeAreRefusedBeforeAnyWrite()
    {
        using var server = new FakeHomeAnytypeServer();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Product,
                [
                    new HomeCreateItem(null, "Ampoule fictive", null),
                    new HomeCreateItem(null, "Ampoules fictives", null),
                ],
                Ct));

        Assert.Contains("(dans ce lot) · Ampoule fictive (product)", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task AnIdeaIsComparedByItsFirstLine()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Idea("idea-1", "Tester des LED fictives\nDétail fictif"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Idea,
                [new HomeCreateItem(null, null, null, Text: "tester des leds fictives\nAutre détail")],
                Ct));

        Assert.Contains("idea-1", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task ACodedTypeKeepsItsCodeAsIdentityAndSkipsTheGuard()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Room("room-yy", "YY", "Autre pièce fictive"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-PS01", "Prise fictive", "room-zz"));

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Point, [new HomeCreateItem("YY-PS01", "Prise fictive", null)], Ct);

        Assert.Single(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task ANameOfAnotherTypeDoesNotCount()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Plant("plant-1", "Ficus fictif"));

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device, [new HomeCreateItem(null, "Ficus fictif", null)], Ct);

        Assert.Single(server.Requests, request => request.Method == "POST");
    }
}

[Trait("Category", "observability")]
public sealed class HomeNameGuardEventTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    [Fact]
    public async Task ARefusedNearNameTellsTheTypeAndTheCountNeverTheName()
    {
        using var listener = new TestEventListener("Deckle-Home");
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Device("device-1", "Visseuse fictive"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new HomeGestures(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace)
                .CreateAsync(HomeSchema.Types.Device, [new HomeCreateItem(null, "Visseuses fictives", null)], Ct));

        EventWrittenEventArgs refused = Assert.Single(
            listener.Events, e => e.EventId == DeckleHomeSource.EvtNameGuardRefused);
        Assert.Equal(EventLevel.Verbose, refused.Level);
        Assert.Equal(HomeSchema.Types.Device, Field(refused, "type_key"));
        Assert.Equal("1", Field(refused, "candidates_count"));
        Assert.DoesNotContain(listener.Events, e => e.Payload!.Any(p =>
            Convert.ToString(p) is { } text
            && (text.Contains("fictive", StringComparison.OrdinalIgnoreCase) || text.Contains("device-1"))));
    }
}
