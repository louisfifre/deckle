using System.Diagnostics.Tracing;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// A list read that meets an empty page while the server still claims more rows
// survives, but its result may be short. That degradation reaches the journal as
// a Warning that names the read and the offset, and nothing the rows hold.
[Trait("Category", "observability")]
public sealed class PagedReadJournalTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    static JsonObject Page(int rows, bool hasMore) => new()
    {
        ["data"] = new JsonArray(Enumerable.Range(0, rows)
            .Select(i => (JsonNode)new JsonObject
            {
                ["object"] = "object",
                ["id"] = $"object-{i:D4}",
                ["name"] = $"Nom privé {i}",
            }).ToArray()),
        ["pagination"] = new JsonObject { ["has_more"] = hasMore },
    };

    [Fact]
    public async Task AnEmptyPageThatStillClaimsMoreWarnsWithTheReadAndTheOffset()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        using var server = new FakeAnytypeServer();
        server.OnSearch(Page(3, hasMore: true));
        // The fake repeats its last answer, so every later request meets this page.
        server.OnSearch(Page(0, hasMore: true));

        await ObjectListing.AllOfTypesAsync(
            new AnytypeApiClient(server.Credentials), FakeAnytypeServer.Space, ["collection"], Ct);

        EventWrittenEventArgs warning = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtPagedReadEndedOnEmptyPage);
        Assert.Equal(EventLevel.Warning, warning.Level);
        Assert.NotEqual((EventTags)0, warning.Tags & ObservationTags.Journal);
        Assert.Equal("object_listing", Field(warning, "read"));
        Assert.Equal("3", Field(warning, "offset"));
        Assert.DoesNotContain("Nom privé", warning.Message + string.Join(' ', warning.Payload!));
    }

    [Fact]
    public async Task AnEmptyPageThatEndsTheListSaysNothing()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        using var server = new FakeAnytypeServer();
        server.OnSearch(Page(0, hasMore: false));

        await ObjectListing.AllOfTypesAsync(
            new AnytypeApiClient(server.Credentials), FakeAnytypeServer.Space, ["collection"], Ct);

        Assert.DoesNotContain(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtPagedReadEndedOnEmptyPage);
    }
}
