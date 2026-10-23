using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.App.Tests;

// The HUD sink calls code it does not own, the HUD. A failure there used to be
// swallowed on the spot, so the flash just went quiet. It now leaves the sink,
// where the dispatcher contains it and opens a sink incident. The case moved
// here from Deckle.Diagnostics.Tests with the sink itself.
[Trait("Category", "regression")]
public sealed class HudFeedbackSinkTests
{
    [Fact]
    public void A_failing_hud_receiver_is_handed_to_the_dispatcher()
    {
        var sink = new HudFeedbackSink(new ThrowingHud());

        Assert.Throws<InvalidOperationException>(() => sink.Write(Entry("feedback")));
    }

    private static EventEntry Entry(string message)
        => new(
            DateTimeOffset.UtcNow,
            "Deckle-Tests",
            HudFeedbackSink.CanonicalEventName,
            EventLevel.Informational,
            EventKeywords.None,
            ObservationKind.Operational,
            message,
            new Dictionary<string, object?>());

    private sealed class ThrowingHud : IHudFeedbackSink
    {
        public void Write(FeedbackEntry entry) => throw new InvalidOperationException("hud down");
    }
}
