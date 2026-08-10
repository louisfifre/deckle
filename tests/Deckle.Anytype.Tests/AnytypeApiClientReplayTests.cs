using System.Net;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "integration")]
public class AnytypeApiClientReplayTests
{
    private const string ObjectId = "bafyreiReplayaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private sealed class MutableEndpointTrust(bool trusted) : IBackendEndpointTrust
    {
        public bool Trusted { get; set; } = trusted;
        public bool IsTrusted() => Trusted;
    }

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "regression")]
    public async Task CreateIsNotReplayedWhenTheProviderReturnsACommitAmbiguousServerError()
    {
        using var server = new FakeAnytypeServer();
        // The response models the dangerous boundary: the provider may have
        // committed the POST before returning 503. A second request would then
        // create a duplicate, so the client must surface the ambiguity.
        server.OnPostObject(new JsonObject(), status: 503);
        using var client = new AnytypeApiClient(server.Credentials);

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CreateObjectAsync(
                new JsonObject { ["type_key"] = DevSpace.Types.Idee, ["name"] = "Une idée" },
                Ct));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Single(server.Requests, request =>
            request.Method == "POST"
            && request.Path.EndsWith("/objects", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task SafePatchIsReplayedAfterATransientServerError()
    {
        using var server = new FakeAnytypeServer();
        server.OnPatchObject(ObjectId, new JsonObject(), status: 503);
        server.OnPatchObject(ObjectId, new JsonObject
        {
            ["object"] = new JsonObject { ["id"] = ObjectId },
        });
        using var client = new AnytypeApiClient(server.Credentials);

        JsonObject updated = await client.UpdateObjectAsync(
            ObjectId,
            new JsonObject { ["name"] = "Après reprise" },
            Ct);

        Assert.Equal(ObjectId, updated["id"]!.GetValue<string>());
        Assert.Equal(2, server.Requests.Count(request =>
            request.Method == "PATCH"
            && request.Path.EndsWith($"/objects/{ObjectId}", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task LostEndpointTrustPreventsTheBearerRequestFromBeingSent()
    {
        using var server = new FakeAnytypeServer();
        var trust = new MutableEndpointTrust(trusted: false);
        using var client = new AnytypeApiClient(server.Credentials, trust);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CreateObjectAsync(
                new JsonObject { ["type_key"] = DevSpace.Types.Idee, ["name"] = "Une idée" },
                Ct));

        Assert.Contains("not owned by a trusted Deckle provider", error.Message);
        Assert.Empty(server.Requests);
    }
}
