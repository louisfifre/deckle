using System.Text.Json;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The per-app decision model and its one-way migration off the v1 allow-list.
[Trait("Category", "unit")]
public sealed class AutocorrectSettingsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void LegacyEnrolledProcessesMigrateIntoApps()
    {
        const string legacy = """{"enabled":true,"enrolledProcesses":["signal","claude"]}""";

        var s = JsonSerializer.Deserialize<AutocorrectSettings>(legacy, Options)!;

        Assert.True(s.Apps["claude"]);
        Assert.True(s.Apps["SIGNAL"]);      // non-default: only migration can set it, and case-insensitively
        Assert.Null(s.EnrolledProcesses);   // folded in and dropped, never written again
    }

    [Fact]
    public void AppsRoundTripAndStayCaseInsensitive()
    {
        const string json = """{"enabled":true,"apps":{"claude":true,"signal":false}}""";

        var s = JsonSerializer.Deserialize<AutocorrectSettings>(json, Options)!;

        Assert.True(s.Apps["Claude"]);       // case-insensitive lookup survives deserialization
        Assert.False(s.Apps["SIGNAL"]);
        Assert.False(s.Apps.ContainsKey("chrome")); // absent = undecided
    }
}
