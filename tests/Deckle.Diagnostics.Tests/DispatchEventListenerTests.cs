using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "observability")]
public sealed class DispatchEventListenerTests
{
    [EventSource(Name = "Deckle-DispatchTests")]
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

    [Fact]
    public void FansOutTheSameBuiltEntryToEveryRegisteredSink()
    {
        using var dispatch = new DispatchEventListener();
        var first = new CollectingSink();
        var second = new CollectingSink();
        dispatch.AddSink(first);
        dispatch.AddSink(second);

        TestSource.Log.ValueObserved("shared");

        EventEntry firstEntry = Assert.Single(first.Entries);
        EventEntry secondEntry = Assert.Single(second.Entries);
        Assert.Same(firstEntry, secondEntry);
        Assert.Equal("Value shared", firstEntry.FormattedMessage);
        Assert.Equal("shared", firstEntry.Payload["value"]);
    }

    [Fact]
    public void RemovedSinkReceivesNoFutureEvents()
    {
        using var dispatch = new DispatchEventListener();
        var sink = new CollectingSink();
        dispatch.AddSink(sink);

        TestSource.Log.ValueObserved("before");
        dispatch.RemoveSink(sink);
        TestSource.Log.ValueObserved("after");

        EventEntry received = Assert.Single(sink.Entries);
        Assert.Equal("Value before", received.FormattedMessage);
    }

    // The dispatcher listens to every Deckle-* source of the process, and the
    // other test classes of this assembly run in parallel and emit on theirs.
    // A sink that wanted everything would collect their events too, so it
    // wants only this class's source.
    private sealed class CollectingSink : ILogSink
    {
        public List<EventEntry> Entries { get; } = [];

        public bool Wants(EventEntry entry) => entry.Provider == "Deckle-DispatchTests";

        public void Write(EventEntry entry) => Entries.Add(entry);
    }
}
