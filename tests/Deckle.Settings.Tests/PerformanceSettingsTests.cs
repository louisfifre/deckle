using System.Text.Json;
using Xunit;

namespace Deckle.Settings.Tests;

// The shared performance mode of the shell settings: off for a user who never
// touched it, and kept across a save and a load.
[Trait("Category", "unit")]
public sealed class PerformanceSettingsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void PerformanceModeIsOffByDefault()
        => Assert.False(new AppSettings().Performance.PerformanceMode);

    [Fact]
    public void AFileWrittenBeforePerformanceModeExistedLoadsWithItOff()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>(
            """{"developer":{"showPlayground":true}}""", Options)!;

        Assert.False(loaded.Performance.PerformanceMode);
    }

    [Fact]
    public void PerformanceModeSurvivesASaveAndALoad()
    {
        var settings = new AppSettings();
        settings.Performance.PerformanceMode = true;

        var loaded = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(settings, Options), Options)!;

        Assert.True(loaded.Performance.PerformanceMode);
    }
}
