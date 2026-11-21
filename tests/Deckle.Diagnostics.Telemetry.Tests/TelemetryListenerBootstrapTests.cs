using System.Diagnostics.Tracing;
using System.Text.Json;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Telemetry.Tests;

// The dictation corpus is the only dataset left: these tests pin its consent
// gate and the settings file it reads.
[Trait("Category", "observability")]
public sealed class TelemetryListenerBootstrapTests
{
    [EventSource(Name = "Deckle-TelemetryTests")]
    private sealed class TestTelemetrySource : EventSource
    {
        public static readonly TestTelemetrySource Log = new();

        private TestTelemetrySource() { }

        [Event(1, Level = EventLevel.Verbose, Tags = ObservationTags.Dataset)]
        public void CorpusAsrRecorded(string bucket, string tier, string text)
        {
            if (IsEnabled()) WriteEvent(1, bucket, tier, text);
        }
    }

    [EventSource(Name = "Deckle-OperationalTelemetryTests")]
    private sealed class OperationalTelemetrySource : EventSource
    {
        public static readonly OperationalTelemetrySource Log = new();

        private OperationalTelemetrySource() { }

        [Event(1, Level = EventLevel.Verbose)]
        public void CorpusAsrRecorded(string bucket, string tier, string text)
        {
            if (IsEnabled()) WriteEvent(1, bucket, tier, text);
        }
    }

    private static string CorpusFile(string root) =>
        Path.Combine(root, "corpus", "bucket", "tier", "corpus.jsonl");

    private static string NewRoot(string label) =>
        Path.Combine(AppContext.BaseDirectory, label + Guid.NewGuid().ToString("N"));

    [Fact]
    public void TheCorpusWaitsForItsConsentAndFollowsIt()
    {
        string root = NewRoot("telemetry-corpus-gate-");
        string corpus = CorpusFile(root);
        var settings = new TelemetrySettings { CorpusEnabled = false };

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(() => settings);

            TestTelemetrySource.Log.CorpusAsrRecorded("bucket", "tier", "while-off");
            dispatch.FlushSinks();
            Assert.False(File.Exists(corpus) && new FileInfo(corpus).Length > 0);

            settings.CorpusEnabled = true;
            TestTelemetrySource.Log.CorpusAsrRecorded("bucket", "tier", "while-on");
            dispatch.FlushSinks();

            string jsonl = SharedFileReader.ReadAllText(corpus);
            Assert.Contains("while-on", jsonl);
            Assert.DoesNotContain("while-off", jsonl);
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConsentedDatasetWritesOnlyExplicitlyTaggedEvents()
    {
        string root = NewRoot("telemetry-dataset-");
        string corpus = CorpusFile(root);

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(
                () => new TelemetrySettings { CorpusEnabled = true });

            OperationalTelemetrySource.Log.CorpusAsrRecorded("bucket", "tier", "operational");
            Assert.False(File.Exists(corpus));

            TestTelemetrySource.Log.CorpusAsrRecorded("bucket", "tier", "dataset");
            dispatch.FlushSinks();

            Assert.True(File.Exists(corpus));
            string jsonl = SharedFileReader.ReadAllText(corpus);
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
        string root = NewRoot("telemetry-gate-throws-");
        string corpus = CorpusFile(root);
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

            TestTelemetrySource.Log.CorpusAsrRecorded("bucket", "tier", "unreadable consent");
            dispatch.FlushSinks();

            // Closed, as when no reader is wired: nothing reaches the disk.
            Assert.False(File.Exists(corpus) && new FileInfo(corpus).Length > 0);
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
        string root = NewRoot("telemetry-closed-");
        string corpus = CorpusFile(root);

        TelemetryListenerBootstrap.ShutDown();
        using var dispatch = new DispatchEventListener();
        try
        {
            TelemetryListenerBootstrap.Configure(
                dispatch, root, validationSubdirectory: false);

            TestTelemetrySource.Log.CorpusAsrRecorded("bucket", "tier", "closed");

            Assert.False(File.Exists(corpus));
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // The microphone level and the two typed-text options are gone, and a
    // settings file written while they existed carries their keys. It must
    // load, keep what a person chose for the corpus, and lose the rest on the
    // next save.
    [Fact]
    [Trait("Category", "regression")]
    public void AFileFromBeforeTheOtherDatasetsWereRemovedStillLoads()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"deckle-telemetry-test-{Guid.NewGuid():N}.json");
        string mutex = $"Deckle-Telemetry-Test-{Guid.NewGuid():N}";
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        File.WriteAllText(path, """
            {
              "microphoneTelemetry": true,
              "corpusEnabled": true,
              "recordAudioCorpus": true,
              "audioCorpusContent": 1,
              "storageDirectory": "relocated",
              "autocorrectDecisions": true,
              "autocorrectText": true
            }
            """);

        try
        {
            using (var store = new JsonSettingsStore<TelemetrySettings>(path, mutex, options))
            {
                Assert.True(store.Current.CorpusEnabled);
                Assert.True(store.Current.RecordAudioCorpus);
                Assert.Equal(AudioCorpusContent.AlwaysRaw, store.Current.AudioCorpusContent);
                Assert.Equal("relocated", store.Current.StorageDirectory);

                store.Save();
                store.Flush();
            }

            string rewritten = File.ReadAllText(path);
            Assert.DoesNotContain("microphoneTelemetry", rewritten);
            Assert.DoesNotContain("autocorrectDecisions", rewritten);
            Assert.DoesNotContain("autocorrectText", rewritten);
            Assert.Contains("corpusEnabled", rewritten);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}