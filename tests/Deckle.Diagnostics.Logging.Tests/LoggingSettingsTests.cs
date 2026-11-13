using System.Text.Json;
using Deckle.Core;
using Xunit;

namespace Deckle.Diagnostics.Logging.Tests;

public sealed class LoggingSettingsTests
{
    private static JsonSerializerOptions Options() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // One level model for every activity: normal by default, with its summary.
    [Fact]
    [Trait("Category", "unit")]
    public void EveryActivityShipsAtItsSummary()
    {
        var settings = new LoggingSettings();

        Assert.False(settings.DetailedLogs);
        foreach (OperationalLogActivity activity in Enum.GetValues<OperationalLogActivity>())
            Assert.Equal(OperationalLogVerbosity.Summary, settings.Verbosity(activity));
    }

    // The switch alone gives every source's detail: each source ships on.
    [Fact]
    [Trait("Category", "unit")]
    public void DetailedLogsAloneGivesEverySourcesDetail()
    {
        var settings = new LoggingSettings { DetailedLogs = true };

        foreach (OperationalLogActivity activity in Enum.GetValues<OperationalLogActivity>())
            Assert.Equal(OperationalLogVerbosity.Details, settings.Verbosity(activity));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ASourceTurnedOffUnderDetailedLogsKeepsItsSummary()
    {
        var settings = new LoggingSettings { DetailedLogs = true, AmbientDetails = false };

        Assert.Equal(OperationalLogVerbosity.Summary, settings.Verbosity(OperationalLogActivity.Ambient));
        Assert.Equal(OperationalLogVerbosity.Details, settings.Verbosity(OperationalLogActivity.Transcription));
    }

    // Off/Summary/Details meant a heartbeat on one card and nothing on
    // another (ambient lighting off had no heartbeat at all; windowing had no
    // summary). No setting reaches Off any more: the summary always passes.
    [Fact]
    [Trait("Category", "regression")]
    public void NoCombinationOfSettingsSilencesAnActivitysSummary()
    {
        foreach (bool detailed in new[] { false, true })
        foreach (bool source in new[] { false, true })
        {
            var settings = new LoggingSettings
            {
                DetailedLogs = detailed,
                AmbientDetails = source, TranscriptionDetails = source, AutocorrectDetails = source,
                InputDetails = source, WindowingDetails = source, LlmDetails = source,
            };
            foreach (OperationalLogActivity activity in Enum.GetValues<OperationalLogActivity>())
                Assert.NotEqual(OperationalLogVerbosity.Off, settings.Verbosity(activity));
        }
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RetentionNamesMatchTheArchiveCaps()
    {
        Assert.Equal(TraceRetention.About60Megabytes, new LoggingSettings().TraceRetention);
        Assert.Equal(10, TraceRetentionPolicy.MaxArchives(TraceRetention.About60Megabytes));
        Assert.Equal(50, TraceRetentionPolicy.MaxArchives(TraceRetention.About300Megabytes));
        Assert.Equal(170, TraceRetentionPolicy.MaxArchives(TraceRetention.About1Gigabyte));
        Assert.Null(TraceRetentionPolicy.MaxArchives(TraceRetention.NoLimit));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void TheSettingsRoundTripThroughTheStoreWithTheRetentionAsAWord()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"deckle-logging-test-{Guid.NewGuid():N}.json");
        string mutex = $"Deckle-Logging-Test-{Guid.NewGuid():N}";

        try
        {
            using var store = new JsonSettingsStore<LoggingSettings>(path, mutex, Options());
            store.Current.DetailedLogs = true;
            store.Current.InputDetails = false;
            store.Current.TraceRetention = TraceRetention.NoLimit;
            store.Flush();

            Assert.Contains("\"traceRetention\": \"NoLimit\"", File.ReadAllText(path));
            using var reloaded = new JsonSettingsStore<LoggingSettings>(path, mutex, Options());
            Assert.True(reloaded.Current.DetailedLogs);
            Assert.False(reloaded.Current.InputDetails);
            Assert.Equal(TraceRetention.NoLimit, reloaded.Current.TraceRetention);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // A file written before the level model keeps loading: its verbosity
    // words are unknown fields, read past, and the activity starts normal.
    [Fact]
    [Trait("Category", "integration")]
    public void AFileFromTheFormerVerbosityModelStillLoads()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"deckle-logging-test-{Guid.NewGuid():N}.json");
        string mutex = $"Deckle-Logging-Test-{Guid.NewGuid():N}";
        File.WriteAllText(path, """
            { "applicationLogToDisk": true, "ambientVerbosity": "Details", "windowingVerbosity": "Off" }
            """);

        try
        {
            using var store = new JsonSettingsStore<LoggingSettings>(path, mutex, Options());
            Assert.True(store.Current.ApplicationLogToDisk);
            Assert.False(store.Current.DetailedLogs);
            Assert.Equal(OperationalLogVerbosity.Summary, store.Current.Verbosity(OperationalLogActivity.Ambient));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
