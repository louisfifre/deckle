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

// Near names: equal fold, same significant words in any order, or a small typo
// that scales with the length of the key. Numbers tell twins apart.
[Trait("Category", "unit")]
public sealed class HomeNearNameTests
{
    [Theory]
    [InlineData("Visseuse fictive", "visseuses  FICTIVES !")]
    [InlineData("Lampe de bureau", "bureau lampe")]
    [InlineData("Lampe de bureau", "La lampe du bureau")]
    [InlineData("Pot pour les fleurs", "fleurs pot")]
    [InlineData("L'ampoule fictive", "ampoule fictive")]
    public void SameSignificantWordsInAnyOrderAreNear(string first, string second) =>
        Assert.True(HomeNearName.AreNear(first, second));

    [Theory]
    [InlineData("Perceuse", "Percuese")]
    [InlineData("Cuisine", "Cuisne")]
    [InlineData("Tournevis", "Tournevsi")]
    [InlineData("Aspirateur balai", "Aspirateur ballai")]
    [InlineData("Aspirateur balai fictif", "Aspiratuer balai fictef")]
    public void ASmallTypoIsNear(string first, string second) =>
        Assert.True(HomeNearName.AreNear(first, second));

    [Theory]
    [InlineData("Vis", "Vit")]
    [InlineData("Clou", "Clef")]
    [InlineData("Perceuse", "Perceuses visseuses")]
    [InlineData("Perceuse", "Pelouse")]
    [InlineData("Aspirateur balai", "Aspirateur ballaix fictif")]
    public void TheToleranceStaysSmall(string first, string second) =>
        Assert.False(HomeNearName.AreNear(first, second));

    [Fact]
    public void ANameOfFourLettersOrFewerTakesNoTypo()
    {
        Assert.False(HomeNearName.AreNear("Vase", "Vasq"));
        Assert.False(HomeNearName.AreNear("Lit", "Lot"));
        Assert.True(HomeNearName.AreNear("Vases", "Vase"));
    }

    [Fact]
    public void AnElevenCharacterKeyTakesTwoTyposButAShorterOneOnlyOne()
    {
        // Key "radiateur chambre" (17) tolerates two, "radiateur" (9) one.
        Assert.True(HomeNearName.AreNear("Radiateur chambre", "Radiateur chmabre"));
        Assert.True(HomeNearName.AreNear("Radiateur chambre", "Radiatuer chmbre"));
        Assert.False(HomeNearName.AreNear("Radiateur", "Radiatuer chmbre"));
        Assert.True(HomeNearName.AreNear("Radiateur", "Radiatuur"));
        Assert.False(HomeNearName.AreNear("Radiateur", "Radiatoor"));
    }

    [Theory]
    [InlineData("Radiateur 1", "Radiateur 2")]
    [InlineData("Radiateur 12", "Radiateur 21")]
    [InlineData("Ampoule E14", "Ampoule E27")]
    [InlineData("Radiateur 1", "Radiateur")]
    [InlineData("Lampe 2 bureau", "Lampe 3 bureau")]
    public void DifferentNumbersAreNeverNear(string first, string second) =>
        Assert.False(HomeNearName.AreNear(first, second));

    [Fact]
    public void TheSameNumbersInAnotherOrderStayNear()
    {
        Assert.True(HomeNearName.AreNear("Lampe bureau 2", "bureau 2 lampe"));
        Assert.True(HomeNearName.AreNear("Radiateur 1", "radiateurs 1"));
    }

    [Fact]
    public void ANameMadeOfStopWordsOnlyKeepsItsFoldedForm()
    {
        Assert.True(HomeNearName.AreNear("Le un", "le  UN"));
        Assert.False(HomeNearName.AreNear("Le un", "la une"));
        Assert.False(HomeNearName.AreNear("Le un", "Lampe"));
    }

    [Fact]
    public void AnEmptyNameIsNearNothing()
    {
        Assert.False(HomeNearName.AreNear("", ""));
        Assert.False(HomeNearName.AreNear("  !!  ", "Lampe"));
        Assert.False(HomeNearName.AreNear((string?)null, (string?)null));
    }

    [Fact]
    public void ADistanceCountsASwapOfNeighboursOnce()
    {
        Assert.Equal(1, HomeNearName.Distance("perceuse", "percuese"));
        Assert.Equal(1, HomeNearName.Distance("lampe", "lamp"));
        Assert.Equal(3, HomeNearName.Distance("abc", "xyz"));
    }
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
    public async Task TheSameWordsInAnotherOrderAreRefusedWithTheCandidate()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Device("device-1", "Lampe de bureau"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Device, [new HomeCreateItem(null, "bureau lampe", null)], Ct));

        Assert.Contains("- device-1 · Lampe de bureau (device)", error.Message);
        Assert.Contains("device_update", error.Message);
        Assert.Contains("force: true", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task ATypoIsRefusedAndForcePassesIt()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Device("device-1", "Perceuse"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Device, [new HomeCreateItem(null, "Percuese", null)], Ct));
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device, [new HomeCreateItem(null, "Percuese", null, Force: true)], Ct);
        Assert.Single(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task NumberedTwinsAreNotFalseDuplicates()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Device("device-1", "Radiateur 1"));

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device, [new HomeCreateItem(null, "Radiateur 2", null)], Ct);

        Assert.Single(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task TwoNumberedTwinsOfOneBatchPassButAnOrderedRepeatIsRefused()
    {
        using var server = new FakeHomeAnytypeServer();

        await Gestures(server).CreateAsync(
            HomeSchema.Types.Device,
            [new HomeCreateItem(null, "Radiateur 1", null), new HomeCreateItem(null, "Radiateur 2", null)],
            Ct);
        Assert.Equal(2, server.Requests.Count(request => request.Method == "POST"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Product,
                [new HomeCreateItem(null, "Lampe de bureau", null), new HomeCreateItem(null, "Bureau lampe", null)],
                Ct));
        Assert.Contains("(dans ce lot) · Lampe de bureau (product)", error.Message);
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
