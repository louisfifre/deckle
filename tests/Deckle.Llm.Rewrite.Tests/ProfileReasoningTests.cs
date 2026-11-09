using System.Text.Json;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// A profile's reasoning level as settings.json holds it. A profile saved
// before the setting existed must read back as "no level", which the engine
// sends as Low and the page shows as Low.
[Trait("Category", "unit")]
public sealed class ProfileReasoningTests
{
    // LlmSettingsService's naming policy.
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void AProfileWithoutTheKeyHasNoLevel()
    {
        var profile = JsonSerializer.Deserialize<RewriteProfile>("""{"name":"Lissage"}""", Options)!;

        Assert.Null(profile.Reasoning);
    }

    [Theory]
    [InlineData(ReasoningLevel.Off)]
    [InlineData(ReasoningLevel.Low)]
    [InlineData(ReasoningLevel.Medium)]
    public void TheLevelIsWrittenByNameAndReadBack(ReasoningLevel level)
    {
        string json = JsonSerializer.Serialize(new RewriteProfile { Reasoning = level }, Options);

        Assert.Contains($"\"reasoning\": \"{level}\"", json.Replace("\":\"", "\": \""));
        Assert.Equal(level, JsonSerializer.Deserialize<RewriteProfile>(json, Options)!.Reasoning);
    }
}
