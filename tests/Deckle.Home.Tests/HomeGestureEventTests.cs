using System.Diagnostics.Tracing;
using Deckle.Anytype;
using Deckle.Home;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Home.Tests;

// Each gesture tells it ran by its name and its duration. What it touched,
// a name, a code or an id read from Home, never travels in an event.
[Trait("Category", "observability")]
public sealed class HomeGestureEventTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static string Field(EventWrittenEventArgs e, string name)
        => Convert.ToString(e.Payload![e.PayloadNames!.IndexOf(name)])!;

    [Fact]
    public async Task TheNewGesturesReportTheirNameAndNothingFromHome()
    {
        using var listener = new TestEventListener("Deckle-Home");
        using var server = new FakeHomeAnytypeServer();
        server.SetObjects(
            FakeHomeAnytypeServer.Room("room-zz", "ZZ", "Pièce fictive"),
            FakeHomeAnytypeServer.Device("device-zz", "Radio fictive"),
            FakeHomeAnytypeServer.ShoppingProduct("product-zz", "Pain fictif", needed: false));
        var gestures = new HomeGestures(new AnytypeApiClient(server.Credentials), FakeHomeAnytypeServer.HomeSpace);

        await gestures.ArchiveAsync("Radio fictive", ct: Ct);
        await gestures.DictatePointsAsync("ZZ", [new HomeCreateItem(null, "Prise fictive", null, Category: "PS")], Ct);
        await gestures.ProductNeedAsync([new HomeProductNeed("Pain fictif")], Ct);
        await gestures.ProductTakenAsync(["Pain fictif"], Ct);
        await gestures.ProductListAsync(ct: Ct);

        string[] completed = listener.Events
            .Where(e => e.EventId == DeckleHomeSource.EvtGestureCompleted)
            .Select(e => Field(e, "gesture"))
            .ToArray();
        Assert.Equal(new[] { "archive", "point_dictate", "product_need", "product_taken", "product_list" }, completed);
        Assert.DoesNotContain(listener.Events, e => e.Payload!.Any(p =>
            Convert.ToString(p) is { } text
            && (text.Contains("fictif", StringComparison.OrdinalIgnoreCase) || text.Contains("-zz")
                || text.Contains("ZZ-PS01") || text.Contains("created-"))));
    }
}
