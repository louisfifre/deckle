using System.Diagnostics.Tracing;
using System.Text.Json.Nodes;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Xunit;

namespace Deckle.Diagnostics.Telemetry.Tests;

[Trait("Category", "regression")]
public sealed class TelemetryListenerBootstrapTests
{
    [EventSource(Name = "Deckle-TelemetryTests")]
    private sealed class TestTelemetrySource : EventSource
    {
        public static readonly TestTelemetrySource Log = new();

        private TestTelemetrySource() { }

        [Event(4, Level = EventLevel.Informational, Message = "test info | text={0}")]
        public void InfoLine(string text)
        {
            if (IsEnabled()) WriteEvent(4, text);
        }

        [Event(1, Level = EventLevel.Verbose, Message = "verbose detail | text={0}")]
        public void VerboseDetail(string text)
        {
            if (IsEnabled()) WriteEvent(1, text);
        }

        [Event(2, Level = EventLevel.Verbose, Message = "asr | text={0}")]
        public void CorpusAsrRecorded(string text)
        {
            if (IsEnabled()) WriteEvent(2, text);
        }

        [Event(3, Level = EventLevel.Verbose, Message = "rewrite | text={0}")]
        public void CorpusRewriteRecorded(string text)
        {
            if (IsEnabled()) WriteEvent(3, text);
        }

        [Event(5, Level = EventLevel.Verbose, Message = "post-dsp | text={0}")]
        public void PreprocessedTelemetryRecorded(string text)
        {
            if (IsEnabled()) WriteEvent(5, text);
        }
    }

    [Fact]
    public void ApplicationLogRespectsRuntimeDropFilter()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-listener-" + Guid.NewGuid().ToString("N"));
        string appLog = Path.Combine(root, "app.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        try
        {
            TelemetryListenerBootstrap.Configure(root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(name => name == "ApplicationLogToDisk");
            TelemetryListenerBootstrap.ConfigureApplicationLogProviderLevelDropFilter(
                (provider, _, _) => provider == "Deckle-TelemetryTests");

            TestTelemetrySource.Log.InfoLine("dropped-by-filter");

            Assert.False(File.Exists(appLog));

            TelemetryListenerBootstrap.ConfigureApplicationLogProviderLevelDropFilter((_, _, _) => false);
            TestTelemetrySource.Log.InfoLine("written-after-filter");

            Assert.True(File.Exists(appLog));
            string jsonl = File.ReadAllText(appLog);
            Assert.Contains("written-after-filter", jsonl);
            Assert.DoesNotContain("dropped-by-filter", jsonl);
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplicationLogWritesReadableJournalMetadata()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-journal-metadata-" + Guid.NewGuid().ToString("N"));
        string appLog = Path.Combine(root, "app.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        try
        {
            TelemetryListenerBootstrap.Configure(root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(name => name == "ApplicationLogToDisk");

            TestTelemetrySource.Log.InfoLine("journal-metadata");

            Assert.True(File.Exists(appLog));
            JsonObject json = JsonNode.Parse(File.ReadAllText(appLog))!.AsObject();
            Assert.Equal("Deckle-TelemetryTests", json["provider"]!.GetValue<string>());
            Assert.Equal("InfoLine", json["event"]!.GetValue<string>());
            Assert.Equal("Informational", json["level"]!.GetValue<string>());
            Assert.Equal("TELEMETRYTESTS", json["source"]!.GetValue<string>());
            Assert.Contains("[TELEMETRYTESTS]", json["line"]!.GetValue<string>());
            Assert.Contains("journal-metadata", json["line"]!.GetValue<string>());
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplicationLogExcludesDedicatedCorpusEvents()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-corpus-exclusion-" + Guid.NewGuid().ToString("N"));
        string appLog = Path.Combine(root, "app.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        try
        {
            TelemetryListenerBootstrap.Configure(root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(name => name == "ApplicationLogToDisk");

            TestTelemetrySource.Log.CorpusAsrRecorded("sensitive-asr-text");
            TestTelemetrySource.Log.CorpusRewriteRecorded("sensitive-rewrite-text");
            TestTelemetrySource.Log.InfoLine("ordinary-log-line");

            string jsonl = File.ReadAllText(appLog);
            Assert.Contains("ordinary-log-line", jsonl);
            Assert.DoesNotContain("sensitive-asr-text", jsonl);
            Assert.DoesNotContain("sensitive-rewrite-text", jsonl);
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PostDspTelemetryRoutesToProcessedFileNotApplicationLog()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-postdsp-" + Guid.NewGuid().ToString("N"));
        string appLog = Path.Combine(root, "app.jsonl");
        string processed = Path.Combine(root, "microphone.processed.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        try
        {
            TelemetryListenerBootstrap.Configure(root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(
                name => name == "ApplicationLogToDisk" || name == "MicrophoneTelemetry");

            TestTelemetrySource.Log.PreprocessedTelemetryRecorded("processed-distribution");
            TestTelemetrySource.Log.InfoLine("ordinary-log-line");

            Assert.True(File.Exists(processed));
            Assert.Contains("processed-distribution", File.ReadAllText(processed));

            string appJsonl = File.ReadAllText(appLog);
            Assert.Contains("ordinary-log-line", appJsonl);
            Assert.DoesNotContain("processed-distribution", appJsonl);
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplicationLogCanShareActivityProjectionWithLogWindow()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "telemetry-activity-filter-" + Guid.NewGuid().ToString("N"));
        string appLog = Path.Combine(root, "app.jsonl");

        TelemetryListenerBootstrap.ShutDown();
        try
        {
            TelemetryListenerBootstrap.Configure(root, validationSubdirectory: false);
            TelemetryListenerBootstrap.ConfigureGates(name => name == "ApplicationLogToDisk");
            TelemetryListenerBootstrap.ConfigureApplicationLogDropFilter(
                entry => !LogWindowFilter.IsVisible(entry, LogWindowVisibilityMode.Activity));

            TestTelemetrySource.Log.VerboseDetail("hidden-verbose");
            TestTelemetrySource.Log.InfoLine("visible-activity");

            string jsonl = File.ReadAllText(appLog);
            Assert.Contains("visible-activity", jsonl);
            Assert.DoesNotContain("hidden-verbose", jsonl);
        }
        finally
        {
            TelemetryListenerBootstrap.ShutDown();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
