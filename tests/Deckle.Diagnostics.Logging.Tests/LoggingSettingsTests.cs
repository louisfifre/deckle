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
    public void InputActivityDefaultsToOff()
    {
        Assert.False(new LoggingSettings().LogInputActivity);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void InputActivityRoundTripsThroughTheSettingsStore()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"deckle-logging-test-{Guid.NewGuid():N}.json");
        string mutex = $"Deckle-Logging-Test-{Guid.NewGuid():N}";

        try
        {
            var store = new JsonSettingsStore<LoggingSettings>(path, mutex, Options());
            store.Current.LogInputActivity = true;
            store.Flush();

            var reloaded = new JsonSettingsStore<LoggingSettings>(path, mutex, Options());
            Assert.True(reloaded.Current.LogInputActivity);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
