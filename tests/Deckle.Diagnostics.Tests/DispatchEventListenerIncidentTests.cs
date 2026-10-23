using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// The listener watching its own sinks: boot replay, sink incidents and the
// bounded flush. Every assertion reads what Deckle-Diagnostics emitted, since
// that is what a reader of the journal would see.
[Trait("Category", "observability")]
[Collection(DiagnosticsProviderCollection.Name)]
public sealed class DispatchEventListenerIncidentTests
{
    [EventSource(Name = "Deckle-DispatchIncidentTests")]
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
    public void ReplaysBootHistoryToASinkRegisteredLater()
    {
        using var dispatch = new DispatchEventListener();

        TestSource.Log.ValueObserved("before any sink");
        var early = new CollectingSink();
        dispatch.AddSink(early);
        TestSource.Log.ValueObserved("after the first sink");

        var late = new CollectingSink();
        dispatch.AddSink(late, replayBoot: true);
        TestSource.Log.ValueObserved("live");

        Assert.Equal(
            ["Value after the first sink", "Value live"],
            early.Entries.Select(e => e.FormattedMessage));
        Assert.Equal(
            ["Value before any sink", "Value after the first sink", "Value live"],
            late.Entries.Select(e => e.FormattedMessage));
    }

    [Fact]
    public void CompleteBootReleasesTheHistory()
    {
        using var dispatch = new DispatchEventListener();
        TestSource.Log.ValueObserved("lost once boot completes");
        dispatch.CompleteBoot();

        var sink = new CollectingSink();
        dispatch.AddSink(sink, replayBoot: true);

        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void ReportsASinkFaultOnceAndItsRecoveryOnce()
    {
        using var dispatch = new DispatchEventListener();
        dispatch.CompleteBoot();
        using var reports = new TestEventListener("Deckle-Diagnostics");
        var sink = new FailingSink();
        dispatch.AddSink(sink);

        sink.Fail = true;
        TestSource.Log.ValueObserved("first failure");
        TestSource.Log.ValueObserved("second failure");
        sink.Fail = false;
        TestSource.Log.ValueObserved("back");
        TestSource.Log.ValueObserved("still fine");

        // Deckle-Diagnostics is process-wide: the JsonlSink tests running in
        // parallel report their own faults there, so keep only this sink's.
        var incident = reports.Events
            .Where(e => e.EventSource.Name == "Deckle-Diagnostics"
                && Equals(e.Payload?[0], nameof(FailingSink)))
            .Select(e => (e.EventName, Payload: e.Payload!.ToArray()))
            .ToArray();

        // One opening, named by the sink and carrying the cause; one closing
        // with the count of failed writes in between, nothing for the rest.
        Assert.Collection(incident,
            opened =>
            {
                Assert.Equal("SinkFaulted", opened.EventName);
                Assert.Equal(nameof(FailingSink), opened.Payload[0]);
                Assert.Equal(nameof(InvalidOperationException), opened.Payload[1]);
                Assert.Equal("sink down", opened.Payload[2]);
            },
            closed =>
            {
                Assert.Equal("SinkRecovered", closed.EventName);
                Assert.Equal(nameof(FailingSink), closed.Payload[0]);
                Assert.Equal(2, closed.Payload[1]);
            });
        Assert.Equal(
            ["Value back", "Value still fine"],
            sink.Written.Select(e => e.FormattedMessage));
    }

    [Fact]
    public void FlushNamesTheSinkThatFailedAndReportsIt()
    {
        using var dispatch = new DispatchEventListener();
        dispatch.CompleteBoot();
        using var reports = new TestEventListener("Deckle-Diagnostics");
        dispatch.AddSink(new BrokenFlushSink());
        dispatch.AddSink(new CollectingSink());

        IReadOnlyList<SinkFlushFailure> failures = dispatch.FlushSinks(TimeSpan.FromSeconds(1));

        SinkFlushFailure failure = Assert.Single(failures);
        Assert.Equal(nameof(BrokenFlushSink), failure.Sink);
        Assert.IsType<IOException>(failure.Error);

        EventWrittenEventArgs report = Assert.Single(
            reports.Events.Where(e => e.EventSource.Name == "Deckle-Diagnostics"));
        Assert.Equal("FlushFailed", report.EventName);
        Assert.Equal(nameof(BrokenFlushSink), report.Payload![0]);
        Assert.Equal(nameof(IOException), report.Payload[1]);
    }

    [Fact]
    public void FlushReportsASinkThatRanOutOfTime()
    {
        using var dispatch = new DispatchEventListener();
        dispatch.CompleteBoot();
        using var reports = new TestEventListener("Deckle-Diagnostics");
        dispatch.AddSink(new SlowFlushSink());

        IReadOnlyList<SinkFlushFailure> failures = dispatch.FlushSinks(TimeSpan.FromMilliseconds(50));

        SinkFlushFailure failure = Assert.Single(failures);
        Assert.Equal(nameof(SlowFlushSink), failure.Sink);
        Assert.Null(failure.Error);

        EventWrittenEventArgs report = Assert.Single(
            reports.Events.Where(e => e.EventSource.Name == "Deckle-Diagnostics"));
        Assert.Equal("FlushTimedOut", report.EventName);
        Assert.Equal(50, report.Payload![1]);
    }

    private sealed class CollectingSink : ILogSink
    {
        public List<EventEntry> Entries { get; } = [];

        public bool Wants(EventEntry entry) => entry.Provider == "Deckle-DispatchIncidentTests";

        public void Write(EventEntry entry) => Entries.Add(entry);
    }

    private sealed class FailingSink : ILogSink
    {
        public volatile bool Fail;
        public List<EventEntry> Written { get; } = [];

        public bool Wants(EventEntry entry) => entry.Provider == "Deckle-DispatchIncidentTests";

        public void Write(EventEntry entry)
        {
            if (Fail) throw new InvalidOperationException("sink down");
            Written.Add(entry);
        }
    }

    private sealed class BrokenFlushSink : IFlushableLogSink
    {
        public bool Wants(EventEntry entry) => false;
        public void Write(EventEntry entry) { }
        public void Flush() => throw new IOException("disk gone");
        public bool TryFlush(TimeSpan timeout) => throw new IOException("disk gone");
        public void Dispose() { }
    }

    private sealed class SlowFlushSink : IFlushableLogSink
    {
        public bool Wants(EventEntry entry) => false;
        public void Write(EventEntry entry) { }
        public void Flush() { }
        public bool TryFlush(TimeSpan timeout) => false;
        public void Dispose() { }
    }
}
