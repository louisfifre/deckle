using System.Text.Json;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// A profile's reasoning level as settings.json holds it and as the page's
// list shows it. A profile saved before the setting existed must read back as
// "no level", which keeps the engine's own default.
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

    [Fact]
    public void TheLevelIsWrittenByNameAndReadBack()
    {
        string json = JsonSerializer.Serialize(new RewriteProfile { Reasoning = ReasoningLevel.Medium }, Options);

        Assert.Contains("\"reasoning\": \"Medium\"", json.Replace("\":\"", "\": \""));
        Assert.Equal(ReasoningLevel.Medium, JsonSerializer.Deserialize<RewriteProfile>(json, Options)!.Reasoning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ReasoningLevel.Off)]
    [InlineData(ReasoningLevel.Low)]
    [InlineData(ReasoningLevel.Medium)]
    [InlineData(ReasoningLevel.High)]
    public void TheListChoiceRoundTripsEveryLevel(ReasoningLevel? level)
    {
        Assert.Equal(level, ReasoningChoices.ToLevel(ReasoningChoices.From(level)));
    }

    [Fact]
    public void AutomaticStoresNoLevel()
    {
        Assert.Null(ReasoningChoices.ToLevel(ReasoningChoice.Automatic));
    }
}
