using Deckle.Anytype;
using Xunit;

namespace Deckle.Home.Tests;

// A write never lands on an object that was only guessed: a name given in full, a
// code or an id resolves, a part of a name is refused with the nearest objects by
// id. A read keeps taking the one object a part of a name points to.
[Trait("Category", "integration")]
public sealed class HomeNameMatchTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    static FakeHomeAnytypeServer ServerWithOneTodo()
    {
        var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Todo("todo-1", "Purger le radiateur fictif", null, done: false));
        return server;
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task APartOfANameDoesNotCompleteTheObjectItPointsTo()
    {
        using var server = ServerWithOneTodo();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CompleteAsync("Purger", Ct));

        Assert.Contains("todo-1", error.Message);
        Assert.Contains("Purger le radiateur fictif", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method is "PATCH" or "POST" or "DELETE");
    }

    [Fact]
    public async Task ANameGivenInFullWritesWhateverItsCase()
    {
        using var server = ServerWithOneTodo();

        await Gestures(server).CompleteAsync("purger le radiateur FICTIF", Ct);

        Assert.Contains(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task AnIdWritesAsItDid()
    {
        using var server = ServerWithOneTodo();

        await Gestures(server).CompleteAsync("todo-1", Ct);

        Assert.Contains(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task APartOfANameDoesNotUpdateTheObjectItPointsTo()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync([new HomeUpdateItem("Pièce", "Pièce renommée", null)], Ct));

        Assert.Contains("room-zz", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task APartOfANameDoesNotPickTheTargetOfARelation()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.EquipmentSystem("system-1", "PC fictif"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CreateComponentAsync("GPU fictif", "PC", null, Ct));

        Assert.Contains("system-1", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "POST");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task APartOfANameDoesNotPickACollection()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Collection("floor-add", "Niveau ajouté"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).UpdateAsync(
                [new HomeUpdateItem("ZZ", null, null, AddToCollections: ["Niveau"])], Ct));

        Assert.Contains("floor-add", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method is "POST" or "PATCH" or "DELETE");
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task APartOfANameDoesNotPreviewADelete()
    {
        using var server = ServerWithOneTodo();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).DeleteAsync("Purger", confirm: false, Ct));

        Assert.Contains("todo-1", error.Message);
    }

    [Fact]
    public async Task AReadStillTakesTheOneObjectAPartOfANamePointsTo()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-PS01", "Prise fictive", "room-zz"),
            FakeHomeAnytypeServer.Todo("todo-1", "Purger le radiateur fictif", null, done: false));

        string detail = await Gestures(server).GetAsync("Purger", Ct);
        string search = await Gestures(server).SearchAsync(
            new HomeSearchFilter(null, null, "Pièce", null, null, null), Ct);

        Assert.Contains("Purger le radiateur fictif", detail);
        Assert.Contains("Prise fictive", search);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AnExactNameIsFoundAmongManyObjectsThatContainIt()
    {
        using var server = new FakeHomeAnytypeServer();
        var objects = Enumerable.Range(1, 40)
            .Select(n => FakeHomeAnytypeServer.Todo($"todo-{n}", $"Peindre le mur fictif {n}", null, done: false))
            .Append(FakeHomeAnytypeServer.Todo("todo-exact", "Peindre", null, done: false))
            .ToArray();
        server.SetObjects(objects);

        await Gestures(server).CompleteAsync("Peindre", Ct);

        FakeHomeAnytypeServer.Received patch = server.Requests.Single(request => request.Method == "PATCH");
        Assert.EndsWith("/objects/todo-exact", patch.Path);
    }

    [Fact]
    public async Task TheCandidateListIsBounded()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(Enumerable.Range(1, 40)
            .Select(n => FakeHomeAnytypeServer.Todo($"todo-{n}", $"Peindre le mur fictif {n}", null, done: false))
            .ToArray());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).CompleteAsync("Peindre", Ct));

        Assert.Equal(20, error.Message.Split('\n').Count(line => line.StartsWith("- todo-", StringComparison.Ordinal)));
    }
}