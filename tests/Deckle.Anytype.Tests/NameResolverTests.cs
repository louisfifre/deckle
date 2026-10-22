using System.Net;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Name resolution over the v2 search (a `query` plus one `type` per search),
// with synthetic rows shaped like the live answers measured on 2026-10-22.
[Trait("Category", "unit")]
public class NameResolverTests
{
    const string FirstId  = "bafyreiNameaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string SecondId = "bafyreiNamebbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static readonly AnytypeCredentials V2Credentials =
        new("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1");

    static NameResolver ResolverOver(FakeV2Handler handler)
    {
        // The v1 client is never reached: the default space rides on v2.
        var v1 = new AnytypeApiClient(V2Credentials);
        return new NameResolver(v1, new AnytypeV2Client(V2Credentials, null, handler));
    }

    static string Rows(params (string Id, string Name, string Type)[] rows) => new JsonObject
    {
        ["data"] = new JsonArray(rows.Select(r => (JsonNode)new JsonObject
        {
            ["id"] = r.Id,
            ["name"] = r.Name,
            ["type"] = r.Type,
        }).ToArray()),
        ["total"] = rows.Length,
        ["has_more"] = false,
    }.ToJsonString();

    [Fact]
    public async Task AnIdIsReturnedAsIsWithoutAnyRequest()
    {
        var handler = new FakeV2Handler();

        string id = await ResolverOver(handler).ResolveAsync(FirstId, [DevSpace.Types.Task], Ct);

        Assert.Equal(FirstId, id);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ANameIsSearchedByQueryAndTypeInTheDefaultSpace()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, Rows((FirstId, "Refonte", "project")));

        string id = await ResolverOver(handler).ResolveAsync(" Refonte ", [DevSpace.Types.Project], Ct);

        Assert.Equal(FirstId, id);
        FakeV2Handler.Seen seen = Assert.Single(handler.Requests);
        Assert.StartsWith("/v2/spaces/space-1/search?", seen.PathAndQuery);
        JsonObject body = (JsonObject)JsonNode.Parse(seen.Body!)!;
        Assert.Equal("Refonte", body["query"]!.GetValue<string>());
        Assert.Equal(DevSpace.Types.Project, body["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheExactNameWinsOverTheOtherCandidates()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, Rows(
            (FirstId, "Refonte de l'API", "project"),
            (SecondId, "refonte", "project")));

        Assert.Equal(SecondId, await ResolverOver(handler).ResolveAsync("Refonte", [DevSpace.Types.Project], Ct));
    }

    [Fact]
    public async Task SeveralExactNamesAreAmbiguousAndListTheirIds()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, Rows(
            (FirstId, "Refonte", "project"),
            (SecondId, "Refonte", "project")));

        AmbiguousNameException error = await Assert.ThrowsAsync<AmbiguousNameException>(
            () => ResolverOver(handler).ResolveAsync("Refonte", [DevSpace.Types.Project], Ct));

        Assert.Contains(FirstId, error.Message);
        Assert.Contains(SecondId, error.Message);
    }

    [Fact]
    public async Task ALoneCandidateIsTakenAndSeveralInexactOnesAreAmbiguous()
    {
        var lone = new FakeV2Handler().Reply(HttpStatusCode.OK, Rows((FirstId, "Refonte de l'API", "project")));
        Assert.Equal(FirstId, await ResolverOver(lone).ResolveAsync("Refonte", [DevSpace.Types.Project], Ct));

        var several = new FakeV2Handler().Reply(HttpStatusCode.OK, Rows(
            (FirstId, "Refonte A", "project"),
            (SecondId, "Refonte B", "project")));
        await Assert.ThrowsAsync<AmbiguousNameException>(
            () => ResolverOver(several).ResolveAsync("Refonte", [DevSpace.Types.Project], Ct));
    }

    [Fact]
    public async Task ANameWithNoHitIsNotFound()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, Rows());

        await Assert.ThrowsAsync<NotFoundException>(
            () => ResolverOver(handler).ResolveAsync("Inconnu", [DevSpace.Types.Task], Ct));
    }

    // The v2 search takes a single type, so each wanted type is one search; the
    // hits are merged and an object found twice counts once.
    [Fact]
    public async Task SeveralTypesCostOneSearchEachAndMergeTheirHits()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, Rows((FirstId, "Cadrage", "task")))
            .Reply(HttpStatusCode.OK, Rows((FirstId, "Cadrage", "task"), (SecondId, "Cadrage", "project")));

        AmbiguousNameException error = await Assert.ThrowsAsync<AmbiguousNameException>(
            () => ResolverOver(handler).ResolveAsync(
                "Cadrage", [DevSpace.Types.Task, DevSpace.Types.Project], Ct));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            [DevSpace.Types.Task, DevSpace.Types.Project],
            handler.Requests.Select(r => ((JsonObject)JsonNode.Parse(r.Body!)!)["type"]!.GetValue<string>()));
        Assert.Equal(2, error.Candidates.Count);
    }

    [Fact]
    public async Task NoTypeFilterSearchesOnceWithoutAType()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, Rows((FirstId, "Idée", "idee")));

        await ResolverOver(handler).ResolveAsync("Idée", null, Ct);

        JsonObject body = (JsonObject)JsonNode.Parse(Assert.Single(handler.Requests).Body!)!;
        Assert.False(body.ContainsKey("type"));
    }

    [Fact]
    public async Task ANamelessObjectReadsAsUntitledInAnAmbiguityMessage()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, Rows(
            (FirstId, "", "rapport"),
            (SecondId, "", "rapport")));

        AmbiguousNameException error = await Assert.ThrowsAsync<AmbiguousNameException>(
            () => ResolverOver(handler).ResolveAsync("Journal", [DevSpace.Types.Rapport], Ct));

        Assert.Contains("(sans titre)", error.Message);
    }

    // The v2 client is bound to one space: another space is searched on v1.
    [Fact]
    public async Task AnotherSpaceIsSearchedThroughV1AndNeverTouchesV2()
    {
        using var server = new FakeAnytypeServer();
        server.OnSearch(new JsonObject
        {
            ["data"] = new JsonArray(new JsonObject
            {
                ["id"] = FirstId,
                ["name"] = "Refonte",
                ["type"] = new JsonObject { ["key"] = DevSpace.Types.Project },
            }),
        });
        var handler = new FakeV2Handler();
        var v1 = new AnytypeApiClient(server.Credentials);
        var resolver = new NameResolver(v1, new AnytypeV2Client(V2Credentials, null, handler));

        string id = await resolver.ResolveAsync(v1.SpaceId, "Refonte", [DevSpace.Types.Project], Ct);

        Assert.Equal(FirstId, id);
        Assert.Empty(handler.Requests);
    }
}
