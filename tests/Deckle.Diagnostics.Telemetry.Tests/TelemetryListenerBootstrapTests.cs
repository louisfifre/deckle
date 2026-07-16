using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
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
            string jsonl = File.ReadAllText(latency);
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
