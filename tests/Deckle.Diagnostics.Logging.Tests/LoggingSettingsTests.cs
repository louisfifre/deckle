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

    [Fact]
    [Trait("Category", "unit")]
    public void ActivitiesShipAtSummaryAndWindowingOff()
    {
        var settings = new LoggingSettings();

        Assert.Equal(OperationalLogVerbosity.Summary, settings.AmbientVerbosity);
        Assert.Equal(OperationalLogVerbosity.Summary, settings.TranscriptionVerbosity);
        Assert.Equal(OperationalLogVerbosity.Summary, settings.AutocorrectVerbosity);
        Assert.Equal(OperationalLogVerbosity.Summary, settings.InputVerbosity);
        Assert.Equal(OperationalLogVerbosity.Off, settings.WindowingVerbosity);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AVerbosityRoundTripsThroughTheSettingsStoreAsAWord()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"deckle-logging-test-{Guid.NewGuid():N}.json");
        string mutex = $"Deckle-Logging-Test-{Guid.NewGuid():N}";

        try
        {
            using var store = new JsonSettingsStore<LoggingSettings>(path, mutex, Options());
            store.Current.InputVerbosity = OperationalLogVerbosity.Details;
            store.Flush();

            Assert.Contains("\"inputVerbosity\": \"Details\"", File.ReadAllText(path));
            using var reloaded = new JsonSettingsStore<LoggingSettings>(path, mutex, Options());
            Assert.Equal(OperationalLogVerbosity.Details, reloaded.Current.InputVerbosity);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AToggleThatWasOnBecomesDetailsAndOneThatWasOffBecomesSummary()
    {
        (string json, bool migrated) = LoggingSettingsService.MigrateDetailToggles(
            """{ "applicationLogToDisk": true, "logTranscriptionActivity": true, "logAmbientCaptureActivity": false }""");

        Assert.True(migrated);
        LoggingSettings settings = JsonSerializer.Deserialize<LoggingSettings>(json, Options())!;
        Assert.Equal(OperationalLogVerbosity.Details, settings.TranscriptionVerbosity);
        Assert.Equal(OperationalLogVerbosity.Summary, settings.AmbientVerbosity);
        Assert.True(settings.ApplicationLogToDisk);
        Assert.DoesNotContain("logTranscriptionActivity", json);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void WindowingOffStaysOffBecauseItHasNoSummary()
    {
        (string json, _) = LoggingSettingsService.MigrateDetailToggles(
            """{ "logWindowingActivity": false }""");

        LoggingSettings settings = JsonSerializer.Deserialize<LoggingSettings>(json, Options())!;
        Assert.Equal(OperationalLogVerbosity.Off, settings.WindowingVerbosity);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ALevelAlreadyInTheFileWinsOverAStaleToggle()
    {
        (string json, _) = LoggingSettingsService.MigrateDetailToggles(
            """{ "logInputActivity": true, "inputVerbosity": "Off" }""");

        LoggingSettings settings = JsonSerializer.Deserialize<LoggingSettings>(json, Options())!;
        Assert.Equal(OperationalLogVerbosity.Off, settings.InputVerbosity);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AFileWithoutTogglesIsLeftAsItIs()
    {
        const string original = """{ "inputVerbosity": "Details" }""";

        (string json, bool migrated) = LoggingSettingsService.MigrateDetailToggles(original);

        Assert.False(migrated);
        Assert.Same(original, json);
    }
}
