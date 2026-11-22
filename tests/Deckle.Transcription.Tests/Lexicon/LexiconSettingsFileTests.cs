using System.Text.Json;
using Xunit;

namespace Deckle.Transcription.Tests.Lexicon;

// The lexicon as the settings file stores it. Version 0.144 wrote a boost
// strength the settings no longer carry; such a file must keep its words.
[Trait("Category", "unit")]
public sealed class LexiconSettingsFileTests
{
    // The options the settings service persists with.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void AFileWithTheRetiredStrengthKeepsItsWordsAndSwitch()
    {
        var settings = JsonSerializer.Deserialize<TranscriptionSettings>(
            """{ "lexicon": { "terms": ["Deckle", "WinUI"], "boostEnabled": false, "boostStrength": 3.5 } }""",
            Options)!;

        Assert.Equal(["Deckle", "WinUI"], settings.Lexicon.Terms);
        Assert.False(settings.Lexicon.BoostEnabled);
    }
}
