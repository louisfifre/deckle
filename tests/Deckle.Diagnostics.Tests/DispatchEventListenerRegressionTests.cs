using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "regression")]
[Collection(DiagnosticsProviderCollection.Name)]
public sealed class DispatchEventListenerRegressionTests
{
    [EventSource(Name = "Deckle-DispatchRegressionTests")]
    private sealed class TestSource : EventSource
    {
        public static readonly TestSource Log = new();

        private TestSource() { }

        [Event(1, Level = EventLevel.Informational, Message = "Value {0}")]
        public void ValueObserved(string value)
        {
            if (IsEnabled()) WriteEvent(1, value);
        }
    }

    private sealed class ThrowingSink : ILogSink
    {
        public bool Wants(EventEntry entry) => entry.Provider == "Deckle-DispatchRegressionTests";

        public void Write(EventEntry entry) => throw new InvalidOperationException("sink down at replay");
    }

    [Fact]
    public void A_sink_fault_during_boot_replay_is_reported_without_waiting_for_a_later_event()
    {
        using var dispatch = new DispatchEventListener();
        using var reports = new TestEventListener("Deckle-Diagnostics");
        TestSource.Log.ValueObserved("replayed to a sink that throws");

        dispatch.AddSink(new ThrowingSink(), replayBoot: true);

        EventWrittenEventArgs opened = Assert.Single(
            reports.Events.Where(e => e.EventName == "SinkFaulted"
                && Equals(e.Payload?[0], nameof(ThrowingSink))));
        Assert.Equal(nameof(InvalidOperationException), opened.Payload![1]);
    }
}
