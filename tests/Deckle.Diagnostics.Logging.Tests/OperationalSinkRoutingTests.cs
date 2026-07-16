using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Diagnostics.Logging.Tests;

[Trait("Category", "observability")]
public sealed class OperationalSinkRoutingTests
{
    [Fact]
    public void ApplicationLogWritesOnlyFutureEnabledOperationalEntries()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "application-log-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "app.jsonl");
        bool enabled = false;
        var sink = new ApplicationLogSink(root, () => enabled);

        try
        {
            var beforeEnable = Entry("before-enable", ObservationKind.Operational);
            Assert.False(sink.Wants(beforeEnable));
            Assert.False(File.Exists(path));

            enabled = true;
            var dataset = Entry("dataset", ObservationKind.Dataset);
            Assert.False(sink.Wants(dataset));

            var admitted = Entry("admitted", ObservationKind.Operational);
            Assert.True(sink.Wants(admitted));
            sink.Write(admitted);
            sink.Flush();

            Assert.True(File.Exists(path));
            string jsonl = File.ReadAllText(path);
            Assert.Contains("admitted", jsonl);
            Assert.DoesNotContain("before-enable", jsonl);
            Assert.DoesNotContain("dataset", jsonl);
        }
        finally
        {
            sink.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplicationLogRecordingFilterIsIndependentFromLogWindowSelection()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "application-filter-" + Guid.NewGuid().ToString("N"));
        var recordingSelection = new LogFilterSelection();
        recordingSelection.Add(new LogFilterToken(
            LogFilterDimension.Severity,
            EventLevel.Warning.ToString()));
        var sink = new ApplicationLogSink(
            root,
            static () => true,
            recordingSelection.Matches);

        try
        {
            LogWindowFilterSession.Selection.Clear();
            LogWindowFilterSession.Selection.Add(new LogFilterToken(
                LogFilterDimension.Severity,
                EventLevel.Verbose.ToString()));

            Assert.False(sink.Wants(Entry("verbose", ObservationKind.Operational)));
            Assert.True(sink.Wants(Entry(
                "warning", ObservationKind.Operational, EventLevel.Warning)));
        }
        finally
        {
            LogWindowFilterSession.Selection.Clear();
            sink.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LogWindowRejectsDatasetEntriesBeforeBuffering()
    {
        var sink = new LogWindowSink();
        var receiver = new CollectingLogWindowSink();
        sink.AttachSink(receiver);

        var dataset = Entry("dataset", ObservationKind.Dataset);
        var operational = Entry("operational", ObservationKind.Operational);
        if (sink.Wants(dataset)) sink.Write(dataset);
        if (sink.Wants(operational)) sink.Write(operational);

        EventEntry received = Assert.Single(receiver.Entries);
        Assert.Equal("operational", received.FormattedMessage);
    }

    [Fact]
    public void LogWindowReplaysOnlyTheNewestCapacityInChronologicalOrder()
    {
        const int capacity = 5000;
        const int overflow = 7;
        var sink = new LogWindowSink();

        for (int i = 0; i < capacity + overflow; i++)
            sink.Write(Entry(i.ToString(), ObservationKind.Operational));

        var receiver = new CollectingLogWindowSink();
        sink.AttachSink(receiver);

        Assert.Equal(capacity, receiver.Entries.Count);
        Assert.Equal(overflow.ToString(), receiver.Entries[0].FormattedMessage);
        Assert.Equal((capacity + overflow - 1).ToString(), receiver.Entries[^1].FormattedMessage);
    }

    [Fact]
    public void LogWindowStopsLiveDeliveryAfterSinkDetaches()
    {
        var sink = new LogWindowSink();
        var receiver = new CollectingLogWindowSink();
        sink.AttachSink(receiver);

        sink.Write(Entry("attached", ObservationKind.Operational));
        sink.DetachSink(receiver);
        sink.Write(Entry("detached", ObservationKind.Operational));

        EventEntry received = Assert.Single(receiver.Entries);
        Assert.Equal("attached", received.FormattedMessage);
    }

    private static EventEntry Entry(
        string message,
        ObservationKind kind,
        EventLevel level = EventLevel.Verbose)
        => new(
            DateTimeOffset.UtcNow,
            "Deckle-Tests",
            "TestEvent",
            level,
            EventKeywords.None,
            kind,
            message,
            new Dictionary<string, object?>());

    private sealed class CollectingLogWindowSink : ILogWindowSink
    {
        public List<EventEntry> Entries { get; } = [];

        public void Write(EventEntry entry) => Entries.Add(entry);
    }
}
