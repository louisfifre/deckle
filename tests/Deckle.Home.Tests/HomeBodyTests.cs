using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "unit")]
public sealed class HomeBodyTests
{
    [Fact]
    public async Task SectionUpdatePreservesOtherContentAndUsesHomeSpace()
    {
        using var server = new FakeHomeAnytypeServer();
        JsonObject task = FakeHomeAnytypeServer.Todo("task-1", "Fictional task", null, false);
        task["markdown"] = "Introduction\n\n## Work\nOld\n\n## Keep\nCollected fact\n";
        server.SetObjects(task);
        using var api = new AnytypeApiClient(server.Credentials);
        var gestures = new HomeGestures(api, FakeHomeAnytypeServer.HomeSpace);
        string result = await gestures.UpdateTypedAsync(HomeSchema.Types.Todo,
            [new HomeUpdateItem("task-1", null, null, Section: new HomeSectionEdit("Work", "New"))],
            TestContext.Current.CancellationToken);
        var patch = server.Requests.Single(request => request.Method == "PATCH");
        Assert.StartsWith("/v1/spaces/" + FakeHomeAnytypeServer.HomeSpace + "/objects/", patch.Path);
        string body = JsonNode.Parse(patch.Body)!["markdown"]!.GetValue<string>();
        Assert.Contains("## Work\nNew", body);
        Assert.Contains("## Keep\nCollected fact", body);
        Assert.StartsWith("Introduction", body);
        Assert.DoesNotContain("diffère", result);
    }

    [Fact]
    public async Task MissingSectionStopsTheWholeBatchBeforeAnyWrite()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Todo("task-1", "First", null, false),
            FakeHomeAnytypeServer.Todo("task-2", "Second", null, false));
        using var api = new AnytypeApiClient(server.Credentials);
        var gestures = new HomeGestures(api, FakeHomeAnytypeServer.HomeSpace);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gestures.UpdateAsync(
            [new HomeUpdateItem("task-1", "New name", null),
             new HomeUpdateItem("task-2", null, null, Section: new HomeSectionEdit("Missing", "Content"))],
            TestContext.Current.CancellationToken));
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public void AppendPreservesTheWholeExistingBody()
    {
        const string existing = "Known facts\n\n## Details\nOriginal text";
        Assert.Equal(existing + "\n\nNew dictated fact", HomeBodyEditor.Edit(existing, "New dictated fact", null));
        Assert.Throws<InvalidOperationException>(() => HomeBodyEditor.Edit(
            "## Work\nFirst\n## Work\nSecond", null, new HomeSectionEdit("Work", "New")));
        Assert.Throws<ArgumentException>(() => HomeBodyEditor.Edit(existing, "Extra", new HomeSectionEdit("Details", "New")));
    }

    [Fact]
    public void ReadbackAcceptsExportEscapingButDetectsLostInformation()
    {
        Assert.True(HomeBodyEditor.Matches("## Details\na_b\n", "## Details  \n\na\\_b  \n"));
        Assert.False(HomeBodyEditor.Matches("## Details\nKnown fact\nNew fact", "## Details\nNew fact"));
    }

    [Fact]
    public async Task ProductCannotBeCompletedAsATask()
    {
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(FakeHomeAnytypeServer.Errand("product-1", "Fictional product", false));
        using var api = new AnytypeApiClient(server.Credentials);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new HomeGestures(api, FakeHomeAnytypeServer.HomeSpace).CompleteAsync("product-1", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }
}
