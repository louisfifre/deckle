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

    // The template names a second argument the event never carries: formatting
    // it throws, which is what a typo in a Message attribute does.
    [EventSource(Name = "Deckle-DispatchBrokenTemplateTests")]
    private sealed class BrokenTemplateSource : EventSource
    {
        public static readonly BrokenTemplateSource Log = new();

        private BrokenTemplateSource() { }

        [Event(1, Level = EventLevel.Informational, Message = "Value {0} and {1}")]
        public void ValueObserved(string value)
        {
            if (IsEnabled()) WriteEvent(1, value);
        }
    }

    private sealed class CollectingSink : ILogSink
    {
        public List<EventEntry> Entries { get; } = [];

        public bool Wants(EventEntry entry) => entry.Provider == "Deckle-DispatchBrokenTemplateTests";

        public void Write(EventEntry entry) => Entries.Add(entry);
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

    [Fact]
    public void A_message_template_that_cannot_be_formatted_is_reported_once_and_the_row_still_arrives()
    {
        using var dispatch = new DispatchEventListener();
        dispatch.CompleteBoot();
        using var reports = new TestEventListener("Deckle-Diagnostics");
        var sink = new CollectingSink();
        dispatch.AddSink(sink);

        BrokenTemplateSource.Log.ValueObserved("first");
        BrokenTemplateSource.Log.ValueObserved("second");

        // The row is not lost: it reaches the sink with its payload, without
        // the rendered message the template could not give.
        Assert.Equal(2, sink.Entries.Count);
        Assert.All(sink.Entries, e => Assert.Null(e.FormattedMessage));
        Assert.Equal("first", sink.Entries[0].Payload["value"]);

        // The provider and event are named once, however often the event fires.
        EventWrittenEventArgs report = Assert.Single(
            reports.Events.Where(e => e.EventName == "MessageTemplateBroken"
                && Equals(e.Payload?[0], "Deckle-DispatchBrokenTemplateTests")));
        Assert.Equal("ValueObserved", report.Payload![1]);
        Assert.Equal(nameof(FormatException), report.Payload[2]);
    }
}
