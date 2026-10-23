using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Telemetry.Tests;

[Trait("Category", "observability")]
public sealed class TelemetryListenerBootstrapTests
{
    [EventSource(Name = "Deckle-TelemetryTests")]
    private sealed class TestTelemetrySource : EventSource
    {
        public static readonly TestTelemetrySource Log = new();

        private TestTelemetrySource() { }

        [Event(1, Level = EventLevel.Verbose, Tags = ObservationTags.Dataset)]
        public void LatencyRecorded(string outcome)
        {
            if (IsEnabled()) WriteEvent(1, outcome);
        }

    }

    [EventSource(Name = "Deckle-OperationalTelemetryTests")]
    private sealed class OperationalTelemetrySource : EventSource
    {
        public static readonly OperationalTelemetrySource Log = new();

        private OperationalTelemetrySource() { }

        [Event(1, Level = EventLevel.Verbose)]
        public void LatencyRecorded(string outcome)
        {
            if (IsEnabled()) WriteEvent(1, outcome);
        }
    }

    [Fact]
    public void ConsentedDatasetWritesOnlyExplicitlyTaggedEvents()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-dataset-" + Guid.NewGuid().ToString("N"));
        string latency = Path.Combine(root, "latency.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(
                name => name == "LatencyEnabled");

            OperationalTelemetrySource.Log.LatencyRecorded("operational");
            Assert.False(File.Exists(latency));

            TestTelemetrySource.Log.LatencyRecorded("dataset");
            dispatch.FlushSinks();

            Assert.True(File.Exists(latency));
            string jsonl = SharedFileReader.ReadAllText(latency);
            Assert.Contains("dataset", jsonl);
            Assert.DoesNotContain("operational", jsonl);
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AConsentReaderThatThrowsWritesNothingAndIsReportedAsASinkIncident()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-gate-throws-" + Guid.NewGuid().ToString("N"));
        string latency = Path.Combine(root, "latency.jsonl");
        string failure = "consent store unreadable " + Guid.NewGuid().ToString("N");

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        using var reports = new TestEventListener("Deckle-Diagnostics");
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(
                _ => throw new InvalidOperationException(failure));

            TestTelemetrySource.Log.LatencyRecorded("unreadable consent");
            dispatch.FlushSinks();

            // Closed, as when no reader is wired: nothing reaches the disk.
            Assert.False(File.Exists(latency) && new FileInfo(latency).Length > 0);
            // And said: the dispatcher names the sink and the cause.
            Assert.Contains(
                reports.Events,
                e => e.EventName == "SinkFaulted" && Equals(e.Payload?[2], failure));
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DatasetConsentDefaultsClosed()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-closed-" + Guid.NewGuid().ToString("N"));
        string latency = Path.Combine(root, "latency.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);

            TestTelemetrySource.Log.LatencyRecorded("closed");

            Assert.False(File.Exists(latency));
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
