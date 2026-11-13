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
        public void MicrophoneTelemetryRecorded(string outcome)
        {
            if (IsEnabled()) WriteEvent(1, outcome);
        }

        [Event(2, Level = EventLevel.Verbose, Tags = ObservationTags.Dataset)]
        public void AutocorrectDecisionRecorded(string outcome)
        {
            if (IsEnabled()) WriteEvent(2, outcome);
        }
    }

    // The microphone level is a developer capture under Detailed logs: its
    // own toggle on is not enough while that switch is off.
    [Fact]
    public void MicrophoneLevelWaitsForDetailedLogs()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-detail-gate-" + Guid.NewGuid().ToString("N"));
        string microphone = Path.Combine(root, "microphone.jsonl");
        bool detailed = false;

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(
                () => new TelemetrySettings { MicrophoneTelemetry = true });
            TelemetryListenerBootstrap.ConfigureDetailGate(() => detailed);

            TestTelemetrySource.Log.MicrophoneTelemetryRecorded("while-off");
            dispatch.FlushSinks();
            Assert.False(File.Exists(microphone) && new FileInfo(microphone).Length > 0);

            detailed = true;
            TestTelemetrySource.Log.MicrophoneTelemetryRecorded("while-on");
            dispatch.FlushSinks();

            string jsonl = SharedFileReader.ReadAllText(microphone);
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
        public void MicrophoneTelemetryRecorded(string outcome)
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
        string microphone = Path.Combine(root, "microphone.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(
                () => new TelemetrySettings { MicrophoneTelemetry = true });
            TelemetryListenerBootstrap.ConfigureDetailGate(() => true);

            OperationalTelemetrySource.Log.MicrophoneTelemetryRecorded("operational");
            Assert.False(File.Exists(microphone));

            TestTelemetrySource.Log.MicrophoneTelemetryRecorded("dataset");
            dispatch.FlushSinks();

            Assert.True(File.Exists(microphone));
            string jsonl = SharedFileReader.ReadAllText(microphone);
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
        string microphone = Path.Combine(root, "microphone.jsonl");
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

            TestTelemetrySource.Log.MicrophoneTelemetryRecorded("unreadable consent");
            dispatch.FlushSinks();

            // Closed, as when no reader is wired: nothing reaches the disk.
            Assert.False(File.Exists(microphone) && new FileInfo(microphone).Length > 0);
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
        string microphone = Path.Combine(root, "microphone.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);

            TestTelemetrySource.Log.MicrophoneTelemetryRecorded("closed");

            Assert.False(File.Exists(microphone));
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
