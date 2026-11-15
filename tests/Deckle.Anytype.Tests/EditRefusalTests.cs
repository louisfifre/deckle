using System.Net;
using System.Net.Http;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "unit")]
public sealed class EditRefusalTests
{
    [Fact]
    public async Task TryAsync_LogsARefusedEditOfEitherClientAndGoesOn()
    {
        var log = new List<string>();

        bool v1 = await EditRefusal.TryAsync(
            () => throw new HttpRequestException("v1", null, HttpStatusCode.Forbidden), "renommer due_date", log);
        bool v2 = await EditRefusal.TryAsync(
            () => throw new AnytypeV2Exception("v2", HttpStatusCode.Forbidden, "forbidden", null, null, []), "vues de task", log);

        Assert.False(v1);
        Assert.False(v2);
        Assert.Equal(
            ["refusé (403) : renommer due_date — à faire dans l'app par le propriétaire de l'espace",
             "refusé (403) : vues de task — à faire dans l'app par le propriétaire de l'espace"],
            log);
    }

    [Fact]
    public async Task TryAsync_LetsAnyOtherFailureStopTheRun()
    {
        var log = new List<string>();

        await Assert.ThrowsAsync<HttpRequestException>(() => EditRefusal.TryAsync(
            () => throw new HttpRequestException("down", null, HttpStatusCode.InternalServerError), "x", log));
        Assert.Empty(log);
    }

    [Fact]
    public void EditKinds_HoldNoCreation()
    {
        Assert.DoesNotContain(SpacePlanExecutor.EditKinds, kind => kind.EndsWith("_create", StringComparison.Ordinal));
    }
}
