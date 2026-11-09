using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// Archive hides what had a life without leaving the space; the bin stays for
// mistakes. Search leaves an archived object out unless asked for it.
[Trait("Category", "integration")]
public sealed class HomeArchiveTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static HomeGestures Gestures(FakeHomeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

    static JsonObject ArchivedEntry(FakeHomeAnytypeServer.Received patch) =>
        JsonNode.Parse(patch.Body)!["properties"]!.AsArray().OfType<JsonObject>()
            .Single(entry => entry["key"]!.GetValue<string>() == HomeSchema.Properties.Archived);

    [Fact]
    public async Task ArchiveTicksTheArchivedCheckboxOfADevice()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Device("device-1", "Radio fictive"));

        string result = await Gestures(server).ArchiveAsync("Radio fictive", ct: Ct);

        Assert.StartsWith("Archivé : Radio fictive", result);
        FakeHomeAnytypeServer.Received patch = Assert.Single(server.Requests, request => request.Method == "PATCH");
        Assert.EndsWith("/objects/device-1", patch.Path);
        Assert.True(ArchivedEntry(patch)["checkbox"]!.GetValue<bool>());
        Assert.DoesNotContain(server.Requests, request => request.Method == "DELETE");
    }

    [Fact]
    public async Task ArchivedFalseBringsTheObjectBack()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Device("device-1", "Radio fictive", archived: true));

        string result = await Gestures(server).ArchiveAsync("device-1", archived: false, Ct);

        Assert.StartsWith("Désarchivé", result);
        Assert.False(ArchivedEntry(Assert.Single(server.Requests, request => request.Method == "PATCH"))["checkbox"]!
            .GetValue<bool>());
    }

    [Theory]
    [InlineData("ZZ-PS01", "un point se supprime")]
    [InlineData("Chantier fictif", "un chantier se termine")]
    [InlineData("ZZ", "une pièce")]
    public async Task ATypeWithoutALifeOfItsOwnRefusesAndSaysWhatToDoInstead(string selector, string guidance)
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Point("point-1", "ZZ-PS01", "Prise fictive", "room-zz"),
            FakeHomeAnytypeServer.Worksite("site-1", "Chantier fictif"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Gestures(server).ArchiveAsync(selector, ct: Ct));

        Assert.Contains("ne s'archive pas", error.Message);
        Assert.Contains(guidance, error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task SearchLeavesArchivedObjectsOutUnlessAskedForThemAlone()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Device("device-1", "Radio fictive"),
            FakeHomeAnytypeServer.Device("device-2", "Lecteur fictif", archived: true));
        HomeGestures gestures = Gestures(server);
        var filter = new HomeSearchFilter(null, HomeSchema.Types.Device, null, null, null);

        string active = await gestures.SearchAsync(filter, Ct);
        string archived = await gestures.SearchAsync(filter with { Archived = true }, Ct);

        Assert.Contains("Radio fictive", active);
        Assert.DoesNotContain("Lecteur fictif", active);
        Assert.Contains("Lecteur fictif", archived);
        Assert.DoesNotContain("Radio fictive", archived);
    }
}
