using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Hud;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Hud.Tests;

// A card that cannot be closed stays on screen with nothing tracking it; the
// journal has to say so without any verbosity control being raised.
[Collection("Hud operational observability")]
[Trait("Category", "observability")]
public sealed class OverlayCardCloseObservabilityTests
{
    [Fact]
    [Trait("Category", "regression")]
    public void AnOverlayCardThatCannotBeClosedIsAWarningInTheJournalWithItsCause()
    {
        OperationalLogAdmission.Configure(static _ => false);
        using var listener = new TestEventListener("Deckle-Hud");

        DeckleHudSource.Log.CleanupFailed("overlay-expiry", nameof(InvalidOperationException), "already closed");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleHudSource.EvtCleanupFailed);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal("overlay-expiry", ev.Payload?[0]);
        Assert.Equal(nameof(InvalidOperationException), ev.Payload?[1]);
    }
}