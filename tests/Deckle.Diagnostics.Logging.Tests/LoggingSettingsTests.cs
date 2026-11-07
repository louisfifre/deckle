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
}
