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

        [Event(2, Level = EventLevel.Verbose, Tags = ObservationTags.Dataset)]
        public void AutocorrectDecisionRecorded(string outcome)
        {
            if (IsEnabled()) WriteEvent(2, outcome);
        }
    }

    // The dictation timings are a developer capture under Detailed logs: their
    // own toggle on is not enough while that switch is off.
    [Fact]
    public void TimingsWaitForDetailedLogs()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-detail-gate-" + Guid.NewGuid().ToString("N"));
        string latency = Path.Combine(root, "latency.jsonl");
        bool detailed = false;

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(
                () => new TelemetrySettings { LatencyEnabled = true });
            TelemetryListenerBootstrap.ConfigureDetailGate(() => detailed);

            TestTelemetrySource.Log.LatencyRecorded("while-off");
            dispatch.FlushSinks();
            Assert.False(File.Exists(latency) && new FileInfo(latency).Length > 0);

            detailed = true;
            TestTelemetrySource.Log.LatencyRecorded("while-on");
            dispatch.FlushSinks();

            string jsonl = SharedFileReader.ReadAllText(latency);
            Assert.Contains("while-on", jsonl);
            Assert.DoesNotContain("while-off", jsonl);
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // A correction decision carries the typed word: it is an option of the
    // typed-text corpus and needs that consent as well as its own.
    [Fact]
    public void DecisionsNeedTheTypedTextConsent()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-decisions-" + Guid.NewGuid().ToString("N"));
        string decisions = Path.Combine(root, "autocorrect.decisions.jsonl");
        var settings = new TelemetrySettings { AutocorrectDecisions = true, AutocorrectText = false };

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(() => settings);

            TestTelemetrySource.Log.AutocorrectDecisionRecorded("without-text");
            dispatch.FlushSinks();
            Assert.False(File.Exists(decisions) && new FileInfo(decisions).Length > 0);

            settings.AutocorrectText = true;
            TestTelemetrySource.Log.AutocorrectDecisionRecorded("with-text");
            dispatch.FlushSinks();

            string jsonl = SharedFileReader.ReadAllText(decisions);
            Assert.Contains("with-text", jsonl);
            Assert.DoesNotContain("without-text", jsonl);
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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
                () => new TelemetrySettings { LatencyEnabled = true });
            TelemetryListenerBootstrap.ConfigureDetailGate(() => true);

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
                () => throw new InvalidOperationException(failure));

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
