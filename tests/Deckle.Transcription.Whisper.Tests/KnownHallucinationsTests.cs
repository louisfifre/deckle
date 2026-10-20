using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

[Trait("Category", "unit")]
public class KnownHallucinationsTests
{
    [Fact]
    public void TheRadioCanadaSignatureMatches()
    {
        Assert.Equal(
            "Sous-titrage Société Radio-Canada",
            KnownHallucinations.Match("Sous-titrage Société Radio-Canada"));
    }

    [Theory]
    [InlineData("sous-titrage société radio-canada")]   // case folded
    [InlineData("Sous-titrage Société Radio-Canada.")]  // trailing period whisper sometimes adds
    [InlineData("  Sous-titrage Société Radio-Canada  ")] // edge whitespace
    [InlineData("❤️ par SousTitreur.com")]              // leading glyph stripped
    public void NormalisationVariantsOfASignatureStillMatch(string text)
    {
        Assert.NotNull(KnownHallucinations.Match(text));
    }

    [Fact]
    public void ARealDictationQuotingThePhraseInContextDoesNotMatch()
    {
        // A real dictation that names the artifact must survive: the whole-segment
        // match keeps the citation when the phrase is embedded in real speech.
        Assert.Null(KnownHallucinations.Match(
            "Comment ça, la putain de filtre de merde, sous-titrage Radio-Canada, là, tu peux pas le mettre, là, enfoiré ?"));
        Assert.Null(KnownHallucinations.Match(
            "C'est toujours le même truc, c'est toujours sous-titrage."));
    }

    [Fact]
    public void APlausiblePhraseDeliberatelyLeftOutDoesNotMatch()
    {
        // "Merci d'avoir regardé cette vidéo" is a documented whisper artifact too,
        // but it is something a user could dictate as a whole utterance, so it is
        // kept out of the catalogue on purpose — the false-positive risk outweighs
        // the catch.
        Assert.Null(KnownHallucinations.Match("Merci d'avoir regardé cette vidéo"));
    }

    [Fact]
    public void OrdinaryTextAndEmptyDoNotMatch()
    {
        Assert.Null(KnownHallucinations.Match("Bonjour, ceci est une vraie dictée."));
        Assert.Null(KnownHallucinations.Match(""));
        Assert.Null(KnownHallucinations.Match("   "));
        Assert.Null(KnownHallucinations.Match("..."));
    }
}

// The 2026-10-21 incident: a credit line reached the delivered text in a variant
// the whole-utterance, edge-trimmed match did not recognise.
[Trait("Category", "regression")]
public class KnownHallucinationsVariantTests
{
    [Theory]
    [InlineData("Sous-titrage. Société Radio-Canada.")]  // period inside the line
    [InlineData("SOUS-TITRAGE SOCIÉTÉ RADIO-CANADA")]   // upper case
    [InlineData("Sous titrage Societe Radio Canada")]    // hyphens and accents lost
    [InlineData(" Sous-titrage  Société Radio-Canada !")] // irregular spacing
    public void ACreditLineMatchesWhateverItsPunctuationCaseOrSpacing(string text)
    {
        Assert.Equal("Sous-titrage Société Radio-Canada", KnownHallucinations.Match(text));
    }

    [Fact]
    public void ACreditSegmentGluedToRealSpeechIsStrippedAndTheSpeechKept()
    {
        TranscriptionSegment[] segments =
        [
            Segment(" Sous-titrage Société Radio-Canada."),
            Segment(" Bonjour, je dicte un vrai texte."),
            Segment(" Et une deuxième phrase."),
        ];

        string text = KnownHallucinations.StripFrom(segments, out var filtered);

        Assert.Equal("Bonjour, je dicte un vrai texte. Et une deuxième phrase.", text);
        FilteredHallucination hit = Assert.Single(filtered);
        Assert.Equal(0, hit.SegmentIndex);
        Assert.Equal("Sous-titrage Société Radio-Canada", hit.Entry);
        Assert.Equal(segments[0].Text.Trim().Length, hit.Characters);
    }

    [Fact]
    public void EachSegmentIsJudgedOnItsOwn()
    {
        TranscriptionSegment[] segments =
        [
            Segment(" Première phrase."),
            Segment(" sous-titrage société radio-canada"),
            Segment(" Dernière phrase, qui cite sous-titrage Société Radio-Canada."),
            Segment(" Amara.org"),
        ];

        string text = KnownHallucinations.StripFrom(segments, out var filtered);

        Assert.Equal(
            "Première phrase. Dernière phrase, qui cite sous-titrage Société Radio-Canada.",
            text);
        Assert.Equal(new[] { 1, 3 }, filtered.Select(f => f.SegmentIndex));
    }

    [Fact]
    public void AnUtteranceThatIsOnlyACreditLineDeliversNoText()
    {
        string text = KnownHallucinations.StripFrom(
            [Segment(" Sous-titrage Société Radio-Canada")], out var filtered);

        Assert.Equal("", text);
        Assert.Single(filtered);
    }

    [Fact]
    public void SegmentsWithoutACreditLineAreDeliveredAsBefore()
    {
        TranscriptionSegment[] segments = [Segment(" Bonjour,"), Segment(" le monde. ")];

        string text = KnownHallucinations.StripFrom(segments, out var filtered);

        Assert.Equal("Bonjour, le monde.", text);
        Assert.Empty(filtered);
    }

    private static TranscriptionSegment Segment(string text) => new(text, 0, 100, 0.9f, 0.1f);
}
