using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// An object id is pasted into a REST path, so a model-supplied id such as
// "../x" must never reach another route. These tests use made-up ids only.
[Trait("Category", "unit")]
public class AnytypeObjectIdTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    const string RealShape = "bafyreimadeupidaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static TheoryData<string?> Refused => new()
    {
        null,
        "",
        "   ",
        "../" + RealShape,
        RealShape + "/../../objects",
        RealShape + "?x=1",
        RealShape + "#frag",
        RealShape + " " + RealShape,
        RealShape + ".suffix",
        "bafy",
        "bafyreiUPPERCASEaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "bafyreidigit0189aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "..",
    };

    [Fact]
    public void ARealIdIsAccepted()
    {
        Assert.True(AnytypeObjectId.Is(RealShape));
        Assert.Equal(RealShape, AnytypeObjectId.Require("  " + RealShape + " ", "id"));
        Assert.Equal(RealShape, AnytypeObjectId.Segment(RealShape, "id"));
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void AMalformedIdIsRefused(string? value)
    {
        Assert.False(AnytypeObjectId.Is(value));
        Assert.Throws<ArgumentException>(() => AnytypeObjectId.Segment(value, "id"));
    }

    // The space id is the one id with a dot (id, dot, suffix): kept, and escaped.
    [Fact]
    public void ASpaceIdWithADotIsEscapedNotRefused()
    {
        Assert.Equal(DevSpace.DevSpaceId, AnytypeObjectId.Escape(DevSpace.DevSpaceId, "spaceId"));
        Assert.Equal("a%2Fb%3Fc%23d%20e", AnytypeObjectId.Escape("a/b?c#d e", "spaceId"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..")]
    [InlineData(".")]
    public void AnEmptyOrDotOnlyValueIsRefusedAsAPathPart(string? value) =>
        Assert.Throws<ArgumentException>(() => AnytypeObjectId.Escape(value, "spaceId"));

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task TheV2ReaderAndWriterSendNothingForAMalformedId(string? value)
    {
        var handler = new FakeV2Handler();
        using var client = new AnytypeV2Client(
            new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"), null, handler);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new AnytypeV2ObjectReader(client).ReadAsync(value!, ct: Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new AnytypeV2Writer(client).ApplyObjectOpsAsync(value!, [], ct: Ct));

        Assert.Empty(handler.Requests);
    }

    // The v1 client: a malformed id is refused before any request leaves.
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task TheV1ClientSendsNothingForAMalformedId(string? value)
    {
        var handler = new CountingHandler();
        using var client = new AnytypeApiClient(
            new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"),
            endpointTrust: null, handler);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.GetObjectAsync(value!, Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.UpdateObjectAsync(value!, new JsonObject(), Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.DeleteObjectAsync(value!, Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => client.RemoveFromCollectionAsync(RealShape, value!, Ct));

        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task ARealIdReachesTheV1ObjectRouteAsOneSegment()
    {
        var handler = new CountingHandler();
        using var client = new AnytypeApiClient(
            new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"),
            endpointTrust: null, handler);

        await client.GetObjectAsync(RealShape, Ct);

        Assert.Equal("/v1/spaces/space-1/objects/" + RealShape, handler.LastPath);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            LastPath = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"object\":{\"id\":\"x\"}}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
