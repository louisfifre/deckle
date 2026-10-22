using System.Net;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "unit")]
public class AnytypeV2ExceptionTests
{
    private static AnytypeV2Exception From(HttpStatusCode status, string body) =>
        AnytypeV2Exception.From("PATCH", "/v2/spaces/s/objects/o", status, body);

    [Fact]
    public void TheEnvelopeIsMappedToStatusCodeMessageAndIssues()
    {
        AnytypeV2Exception error = From(
            HttpStatusCode.BadRequest,
            """{"status":400,"code":"validation_failed","message":"bad op","hint":"fix it","issues":[{"path":"ops[1].match","message":"matches none","hint":"use id"}]}""");

        Assert.Equal(400, error.Status);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("bad op", error.ApiMessage);
        Assert.Equal("fix it", error.Hint);
        AnytypeV2Issue issue = Assert.Single(error.Issues);
        Assert.Equal("ops[1].match", issue.Path);
        Assert.Equal("use id", issue.Hint);
        Assert.Contains("validation_failed", error.Message);
        Assert.Contains("bad op", error.Message);
    }

    [Fact]
    public void TheMessageNamesTheRefusedKeyAndTheHintsWithAtMostThreeIssues()
    {
        AnytypeV2Exception error = From(
            HttpStatusCode.BadRequest,
            """{"code":"validation_failed","message":"invalid","hint":"see docs","issues":[{"path":"properties.foo","message":"unknown key","hint":"use name"},{"path":"b","message":"m2"},{"path":"c","message":"m3"},{"path":"d","message":"m4"}]}""");

        Assert.Contains("properties.foo: unknown key (use name)", error.Message);
        Assert.Contains("b: m2", error.Message);
        Assert.Contains("c: m3", error.Message);
        Assert.DoesNotContain("m4", error.Message);
        Assert.Contains("see docs", error.Message);
        Assert.Equal(4, error.Issues.Count);
    }

    [Fact]
    public void AFailedOpIsLocatedByTheIndexInTheFirstIssuePath()
    {
        AnytypeV2Exception error = From(
            HttpStatusCode.BadRequest,
            """{"code":"validation_failed","issues":[{"path":"ops[7].set.checked","message":"x"}]}""");

        Assert.Equal(7, error.OpIndex);
    }

    [Fact]
    public void ARequestLevelErrorHasNoOpIndex()
    {
        AnytypeV2Exception error = From(
            HttpStatusCode.BadRequest,
            """{"code":"ambiguous_input","message":"outline and block"}""");

        Assert.Null(error.OpIndex);
    }

    [Fact]
    public void AStaleEtagIsA409WithTheEtagMismatchCode()
    {
        AnytypeV2Exception stale = From(
            HttpStatusCode.Conflict, $$"""{"code":"{{AnytypeV2Exception.EtagMismatchCode}}","message":"stale"}""");
        AnytypeV2Exception notStale = From(
            HttpStatusCode.Conflict, $$"""{"code":"{{AnytypeV2Exception.IdempotencyConflictCode}}"}""");

        Assert.True(stale.IsStaleEtag);
        Assert.False(stale.IsIdempotencyConflict);
        Assert.True(notStale.IsIdempotencyConflict);
        Assert.False(notStale.IsStaleEtag);
    }

    [Fact]
    public void ABareConflictIsNeitherStaleNorAnIdempotencyConflict()
    {
        AnytypeV2Exception error = From(HttpStatusCode.Conflict, "");

        Assert.False(error.IsStaleEtag);
        Assert.False(error.IsIdempotencyConflict);
    }

    [Fact]
    public void ARateLimitIsA429()
    {
        Assert.True(From(HttpStatusCode.TooManyRequests, "{}").IsRateLimited);
    }

    [Fact]
    public void ANonJsonBodyStillYieldsAnExceptionWithABoundedExcerpt()
    {
        AnytypeV2Exception error = From(HttpStatusCode.BadGateway, new string('x', 5000));

        Assert.Null(error.Code);
        Assert.True(error.Message.Length < 500);
    }
}
