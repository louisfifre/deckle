using System.Text.Json;
using Xunit;

namespace Deckle.Settings.Tests;

// The Performance mode was removed on 2026-11-25 (Ambient captures at 60
// frames a second whatever it said). A settings file written while it
// existed still carries its key: it loads, the rest of the file is read as
// usual, and nothing turns up in its place.
[Trait("Category", "unit")]
public sealed class RetiredPerformanceModeTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void AFileThatStillCarriesThePerformanceModeLoadsAndKeepsTheRest()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>(
            """{"performance":{"performanceMode":true},"developer":{"developerMode":true}}""", Options)!;

        Assert.True(loaded.Developer.DeveloperMode);
    }
}
