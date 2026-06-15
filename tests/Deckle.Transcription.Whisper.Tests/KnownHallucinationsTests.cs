using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// KnownHallucinations is internal static in Deckle.Transcription.Whisper; the
// test project reaches it through InternalsVisibleTo in that module's csproj. We
// test the behaviour — does a whole-utterance artifact match, does a real
// dictation that merely quotes the phrase stay clear — never the normalisation
// internals, so the tests survive a refactor of the matching.
[Trait("Category", "unit")]
public class KnownHallucinationsTests
{
    [Fact]
    public void TheRadioCanadaSignatureMatches()
    {
        Assert.True(KnownHallucinations.Matches("Sous-titrage Société Radio-Canada"));
    }

    [Theory]
    [InlineData("sous-titrage société radio-canada")]   // case folded
    [InlineData("Sous-titrage Société Radio-Canada.")]  // trailing period whisper sometimes adds
    [InlineData("  Sous-titrage Société Radio-Canada  ")] // edge whitespace
    [InlineData("❤️ par SousTitreur.com")]              // leading glyph stripped
    public void NormalisationVariantsOfASignatureStillMatch(string text)
    {
        Assert.True(KnownHallucinations.Matches(text));
    }

    [Fact]
    public void ARealDictationQuotingThePhraseInContextDoesNotMatch()
    {
        // Verbatim from the logged corpus (2026-06): a real dictation that names
        // the artifact must survive. Whole-utterance matching is exactly what
        // keeps the citation when the phrase is embedded in real speech.
        Assert.False(KnownHallucinations.Matches(
            "Comment ça, la putain de filtre de merde, sous-titrage Radio-Canada, là, tu peux pas le mettre, là, enfoiré ?"));
        Assert.False(KnownHallucinations.Matches(
            "C'est toujours le même truc, c'est toujours sous-titrage."));
    }

    [Fact]
    public void APlausiblePhraseDeliberatelyLeftOutDoesNotMatch()
    {
        // "Merci d'avoir regardé cette vidéo" is a documented whisper artifact too,
        // but it is something a user could dictate as a whole utterance, so it is
        // kept out of the catalogue on purpose — the false-positive risk outweighs
        // the catch.
        Assert.False(KnownHallucinations.Matches("Merci d'avoir regardé cette vidéo"));
    }

    [Fact]
    public void OrdinaryTextAndEmptyDoNotMatch()
    {
        Assert.False(KnownHallucinations.Matches("Bonjour, ceci est une vraie dictée."));
        Assert.False(KnownHallucinations.Matches(""));
        Assert.False(KnownHallucinations.Matches("   "));
    }
}
