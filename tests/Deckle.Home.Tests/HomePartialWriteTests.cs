using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// A batch gesture is several requests with no transaction behind them. These
// tests make one request fail part way and check that the caller is told what
// the space now holds, so repeating the whole batch is not the only option.
[Trait("Category", "regression")]
public class HomePartialWriteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    private static bool IsCreate(FakeHomeAnytypeServer.Received request) =>
        request.Method == "POST" && request.Path.EndsWith("/objects", StringComparison.Ordinal)
        && !request.Path.Contains("/lists/", StringComparison.Ordinal);

    private static bool IsMembership(FakeHomeAnytypeServer.Received request) =>
        request.Method == "POST" && request.Path.Contains("/lists/", StringComparison.Ordinal);

    private static FakeHomeAnytypeServer ServerWithARoomAndACollection()
    {
        var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Collection("floor-1", "Niveau fictif"));
        return server;
    }

    [Fact]
    public async Task CreateReportsWhatWasCreatedWhenALaterItemFails()
    {
        using FakeHomeAnytypeServer server = ServerWithARoomAndACollection();
        int creates = 0;
        server.FailNext(request => IsCreate(request) && ++creates == 2);

        HomePartialWriteException error = await Assert.ThrowsAsync<HomePartialWriteException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Point,
                [
                    new HomeCreateItem("ZZ-PS01", "Prise un", null, Collections: ["Niveau fictif"]),
                    new HomeCreateItem("ZZ-PS02", "Prise deux", null, Collections: ["Niveau fictif"]),
                    new HomeCreateItem("ZZ-PS03", "Prise trois", null),
                ],
                Ct));

        Assert.Equal("create", error.Gesture);
        Assert.Contains("ZZ-PS01", Assert.Single(error.Applied));
        Assert.Contains("ZZ-PS02", error.Interrupted);
        Assert.Contains("ZZ-PS03", Assert.Single(error.NotApplied));
        Assert.Contains("ne crée que ce qui manque", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(error.InnerException);
        // The third item was never attempted.
        Assert.Equal(2, server.Requests.Count(IsCreate));
    }

    [Fact]
    public async Task CreateStillAttachesTheObjectsItCreatedBeforeItFailed()
    {
        using FakeHomeAnytypeServer server = ServerWithARoomAndACollection();
        int creates = 0;
        server.FailNext(request => IsCreate(request) && ++creates == 2);

        HomePartialWriteException error = await Assert.ThrowsAsync<HomePartialWriteException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Point,
                [
                    new HomeCreateItem("ZZ-PS01", "Prise un", null, Collections: ["Niveau fictif"]),
                    new HomeCreateItem("ZZ-PS02", "Prise deux", null, Collections: ["Niveau fictif"]),
                ],
                Ct));

        FakeHomeAnytypeServer.Received membership = Assert.Single(server.Requests, IsMembership);
        Assert.Contains("created-1", membership.Body);
        Assert.Contains("appliquée", error.Message);
    }

    [Fact]
    public async Task CreateTellsTheCallerWhenOnlyTheCollectionMembershipFailed()
    {
        using FakeHomeAnytypeServer server = ServerWithARoomAndACollection();
        server.FailNext(IsMembership);

        HomePartialWriteException error = await Assert.ThrowsAsync<HomePartialWriteException>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Point,
                [new HomeCreateItem("ZZ-PS01", "Prise un", null, Collections: ["Niveau fictif"])],
                Ct));

        Assert.Contains("ZZ-PS01", Assert.Single(error.Applied));
        Assert.Null(error.Interrupted);
        Assert.Empty(error.NotApplied);
        Assert.Contains("non confirmée", error.Message);
        Assert.Contains("add_to_collections", error.Message);
    }

    [Fact]
    public async Task CreateRethrowsTheOriginalFailureWhenNothingWasWritten()
    {
        using FakeHomeAnytypeServer server = ServerWithARoomAndACollection();
        server.FailNext(IsCreate);

        Exception error = await Assert.ThrowsAnyAsync<Exception>(() =>
            Gestures(server).CreateAsync(
                HomeSchema.Types.Point,
                [new HomeCreateItem("ZZ-PS01", "Prise un", null)], Ct));

        Assert.IsNotType<HomePartialWriteException>(error);
    }

    [Fact]
    public async Task UpdateReportsWhichItemsWereWrittenWhenALaterOneFails()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Plant("plant-1", "Ficus fictif"),
            FakeHomeAnytypeServer.Plant("plant-2", "Cactus fictif"),
            FakeHomeAnytypeServer.Plant("plant-3", "Lierre fictif"));
        int patches = 0;
        server.FailNext(request => request.Method == "PATCH" && ++patches == 2);

        HomePartialWriteException error = await Assert.ThrowsAsync<HomePartialWriteException>(() =>
            Gestures(server).UpdateAsync(
                [
                    new HomeUpdateItem("plant-1", "Ficus renommé", null),
                    new HomeUpdateItem("plant-2", "Cactus renommé", null),
                    new HomeUpdateItem("plant-3", "Lierre renommé", null),
                ],
                Ct));

        Assert.Equal("update", error.Gesture);
        Assert.Contains("Ficus fictif", Assert.Single(error.Applied));
        Assert.Contains("Cactus fictif", error.Interrupted);
        Assert.Contains("Lierre fictif", Assert.Single(error.NotApplied));
        Assert.Equal(2, server.Requests.Count(request => request.Method == "PATCH"));
    }

    [Fact]
    public async Task UpdateNamesTheInterruptedItemWhenItsCollectionStepFailsAfterItsWrite()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Collection("floor-1", "Niveau fictif"),
            FakeHomeAnytypeServer.Plant("plant-1", "Ficus fictif"));
        server.FailNext(IsMembership);

        HomePartialWriteException error = await Assert.ThrowsAsync<HomePartialWriteException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("plant-1", "Ficus renommé", null, AddToCollections: ["Niveau fictif"])],
                Ct));

        Assert.Empty(error.Applied);
        Assert.Contains("Ficus fictif", error.Interrupted);
        Assert.Contains("collections", error.Message);
        // The rename went through before the membership failed.
        Assert.Contains(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task UpdateRethrowsTheOriginalFailureWhenNothingWasWritten()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Plant("plant-1", "Ficus fictif"));
        server.FailNext(request => request.Method == "PATCH");

        Exception error = await Assert.ThrowsAnyAsync<Exception>(() =>
            Gestures(server).UpdateAsync([new HomeUpdateItem("plant-1", "Ficus renommé", null)], Ct));

        Assert.IsNotType<HomePartialWriteException>(error);
    }
}