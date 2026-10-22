using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// The UI-facing sinks call code they do not own (a log window, the HUD). A
// failure there used to be swallowed on the spot, so the surface just went
// quiet. It now leaves the sink, where the dispatcher contains it and opens a
// sink incident.
[Trait("Category", "regression")]
[Collection(DiagnosticsProviderCollection.Name)]
public sealed class SinkCallbackFaultTests
{
    [Fact]
    public void A_failing_log_window_surface_is_handed_to_the_dispatcher_and_does_not_starve_the_others()
    {
        var sink = new LogWindowSink();
        var healthy = new CollectingSurface();
        sink.AttachSink(new ThrowingSurface());
        sink.AttachSink(healthy);

        Assert.Throws<InvalidOperationException>(() => sink.Write(Entry("row")));

        Assert.Single(healthy.Entries);
    }

    [Fact]
    public void A_log_window_surface_that_fails_on_replay_is_reported_and_still_attached()
    {
        var sink = new LogWindowSink();
        sink.Write(Entry("history"));
        using var reports = new TestEventListener("Deckle-Diagnostics");
        var surface = new ThrowingSurface("replay down " + Guid.NewGuid().ToString("N"));

        sink.AttachSink(surface);

        EventWrittenEventArgs report = Assert.Single(
            reports.Events,
            e => e.EventName == "SinkFaulted"
                && Equals(e.Payload?[0], nameof(LogWindowSink))
                && Equals(e.Payload?[2], surface.Message));
        Assert.Equal(nameof(InvalidOperationException), report.Payload![1]);
        Assert.Throws<InvalidOperationException>(() => sink.Write(Entry("live")));
    }

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

    private sealed class CollectingSurface : ILogWindowSink
    {
        public List<EventEntry> Entries { get; } = [];

        public void Write(EventEntry entry) => Entries.Add(entry);
    }

    private sealed class ThrowingSurface(string message = "surface down") : ILogWindowSink
    {
        public string Message { get; } = message;

        public void Write(EventEntry entry) => throw new InvalidOperationException(Message);
    }

    private sealed class ThrowingHud : IHudFeedbackSink
    {
        public void Write(FeedbackEntry entry) => throw new InvalidOperationException("hud down");
    }
}
