using Deckle.Transcription.Lexicon;
using Xunit;

namespace Deckle.Transcription.Tests.Lexicon;

[Trait("Category", "unit")]
public sealed class LexiconTermsTests
{
    [Fact]
    public void EntriesAreTrimmedAndInnerSpacesCollapsed()
    {
        Assert.Equal(["Louis Fifre"], LexiconTerms.Merge(["  Louis \t  Fifre "]));
    }

    [Fact]
    public void EmptyAndOverLongEntriesAreDropped()
    {
        string tooLong = new('a', LexiconTerms.MaxTermLength + 1);

        Assert.Equal(["Deckle"], LexiconTerms.Merge(["", "   ", tooLong, "Deckle"]));
    }

    [Fact]
    public void TheFirstSpellingOfACaseInsensitiveDuplicateWins()
    {
        // The person's own entries come first, so their spelling beats a learned one.
        IReadOnlyList<string> merged = LexiconTerms.Merge(["WinUI"], ["Winui", "Deckle"]);

        Assert.Equal(["WinUI", "Deckle"], merged);
    }

    [Fact]
    public void AMissingSourceIsSkipped()
    {
        Assert.Equal(["Deckle"], LexiconTerms.Merge(null, ["Deckle"]));
    }
}
