using System;
using System.Collections.Generic;
using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The dictation lexicon reaches the correctors through PersonalLexiconWithTerms:
// a declared word is adopted at once, live, and only single words take part.
[Trait("Category", "unit")]
public class PersonalLexiconWithTermsTests
{
    private static readonly IPersonalLexicon Empty = new EmptyPersonal();

    [Fact]
    public void ADeclaredWordIsNeverCorrectedAway()
    {
        var french = FrequencyLexicon.LoadTsv(new StringReader("bonjour\t300\n"));
        var declared = new PersonalLexiconWithTerms(Empty, () => ["Bonjuor"]);

        Assert.NotNull(new ConservativeTypoCorrector(french, null, Empty, null).Evaluate("bonjuor", []));
        Assert.Null(new ConservativeTypoCorrector(french, null, declared, null).Evaluate("bonjuor", []));
    }

    [Fact]
    public void AnEditToTheListAppliesAtTheNextCall()
    {
        var terms = new List<string>();
        var lexicon = new PersonalLexiconWithTerms(Empty, () => terms);

        Assert.False(lexicon.IsAdopted("deckle"));
        terms.Add("Deckle");
        Assert.True(lexicon.IsAdopted("deckle"));
    }

    [Fact]
    public void APhraseDoesNotTakePart()
    {
        var lexicon = new PersonalLexiconWithTerms(Empty, () => ["Louis Fifre"]);

        Assert.False(lexicon.IsAdopted("louis fifre"));
        Assert.Empty(lexicon.AdoptedWords);
    }

    [Fact]
    public void AnUnreadableListLeavesTheDictionaryAlone()
    {
        var lexicon = new PersonalLexiconWithTerms(Empty, () => throw new InvalidOperationException());

        Assert.False(lexicon.IsAdopted("deckle"));
        Assert.Empty(lexicon.AdoptedWords);
    }

    private sealed class EmptyPersonal : IPersonalLexicon
    {
        public bool IsAdopted(string word) => false;
        public bool IsSuppressed(string original, string replacement) => false;
        public IReadOnlyCollection<string> AdoptedWords => [];
    }
}
