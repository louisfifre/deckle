using System.Text.Json;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The per-app decision model.
[Trait("Category", "unit")]
public sealed class AutocorrectSettingsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void AppsRoundTripAndStayCaseInsensitive()
    {
        const string json = """{"enabled":true,"apps":{"claude":true,"signal":false}}""";

        var s = JsonSerializer.Deserialize<AutocorrectSettings>(json, Options)!;

        Assert.True(s.Apps["Claude"]);
        Assert.False(s.Apps["SIGNAL"]);
        Assert.False(s.Apps.ContainsKey("chrome")); // absent = undecided
    }
}
