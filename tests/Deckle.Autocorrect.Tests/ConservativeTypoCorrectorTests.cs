using System.Collections.Generic;
using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The conservative typo corrector: stage two, acting only on NON-words and only
// when one common French word sits a single edit away and clearly dominates.
// Conservativity is the product, so — as with the diacritics gate — most of
// these assert a word is LEFT ALONE; the few corrections are the ones that
// earned it. Edits modelled: deletion, transposition, keyboard-adjacent
// substitution, insertion.
[Trait("Category", "unit")]
public class ConservativeTypoCorrectorTests
{
    // Frequencies per million, Lexique scale. ballet/billet are the synthetic
    // ambiguity pair (both one insertion from "bllet", equal frequency); obscur
    // is the below-floor rarity case; monde pairs with the English "mode" for
    // the bilingual guard.
    private const string FrenchTsv =
        "bonjour\t300\nballet\t100\nbillet\t100\nobscur\t2\nchat\t90\nmonde\t100\n";

    private const string EnglishTsv = "the\t60000\nmode\t500\n";

    private static ConservativeTypoCorrector Corrector(
        string? frenchTsv = null,
        FrequencyLexicon? english = null,
        IPersonalLexicon? personal = null,
        TypoOptions? options = null)
    {
        var french = FrequencyLexicon.LoadTsv(new StringReader(frenchTsv ?? FrenchTsv));
        return new ConservativeTypoCorrector(french, english, personal, options);
    }

    // ── Corrections that earned it ──────────────────────────────────────────

    [Fact]
    public void CorrectsTransposition()
    {
        // "bonjuor" — the u/o keys swapped — is one transposition from "bonjour".
        var d = Corrector().Evaluate("bonjuor", []);

        Assert.NotNull(d);
        Assert.Equal("bonjour", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    [Fact]
    public void CorrectsMissingLetter()
    {
        // "bonjor" — a dropped u — is one insertion from "bonjour".
        var d = Corrector().Evaluate("bonjor", []);

        Assert.Equal("bonjour", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    [Fact]
    public void CorrectsAdjacentSubstitution()
    {
        // "bobjour" — n typed as the touching key b — is one adjacent substitution
        // from "bonjour".
        var d = Corrector().Evaluate("bobjour", []);

        Assert.Equal("bonjour", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    [Fact]
    public void PreservesSentenceInitialCase()
    {
        // Sentence-initial (no left context): a capitalised typo is still fixed,
        // and the case is carried over.
        var d = Corrector().Evaluate("Bonjuor", []);

        Assert.Equal("Bonjour", d!.Replacement);
    }

    [Fact]
    public void DominantNeighbourWins()
    {
        // ballet 2000 vs billet 100: ratio 20 clears the 10× bar, so "bllet" is
        // resolved rather than left ambiguous.
        var d = Corrector("ballet\t2000\nbillet\t100\n").Evaluate("bllet", []);

        Assert.Equal("ballet", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    // ── Conservativity: the cases left alone ────────────────────────────────

    [Fact]
    public void ValidFrenchWordIsNeverTouched()
    {
        // "chat" is a valid form — the defining gate: this stage acts on non-words.
        Assert.Null(Corrector().Evaluate("chat", []));
    }

    [Fact]
    public void NonAdjacentSubstitutionIsNotAPlausibleSlip()
    {
        // "zonjour" → "bonjour" needs z→b, but z and b do not touch on QWERTY,
        // so it is not a candidate and the literal stays.
        Assert.Null(Corrector().Evaluate("zonjour", []));
    }

    [Fact]
    public void AmbiguousNeighboursAreLeftAlone()
    {
        // "bllet" is one insertion from both ballet (100) and billet (100):
        // ratio 1, far under 10× — real ambiguity, leave it.
        Assert.Null(Corrector().Evaluate("bllet", []));
    }

    [Fact]
    public void RareNeighbourBelowFloorIsLeftAlone()
    {
        // "obscru" → obscur, but obscur (2) sits under the 5/million floor:
        // not common enough to be the obvious intent.
        Assert.Null(Corrector().Evaluate("obscru", []));
    }

    [Fact]
    public void ShortWordIsLeftAlone()
    {
        // "cha" is below MinWordLength — too little signal — even though "chat"
        // is one insertion away.
        Assert.Null(Corrector().Evaluate("cha", []));
    }

    [Fact]
    public void ProperNounMidSentenceIsLeftAlone()
    {
        // A capitalised token mid-utterance is a name, not a typo to fix.
        Assert.Null(Corrector().Evaluate("Bonjuor", ["salut"]));
    }

    [Fact]
    public void FrequentEnglishWordIsNotFrenchified()
    {
        // "mode" is a frequent English word (and one insertion from "monde");
        // the bilingual guard leaves it alone.
        var english = FrequencyLexicon.LoadTsv(new StringReader(EnglishTsv));
        Assert.Null(Corrector(english: english).Evaluate("mode", []));
    }

    [Fact]
    public void AdoptedWordShieldsItself()
    {
        // The user adopted "bonjuor" as their own — never spell-fix it.
        var personal = new StubPersonal(adopted: new() { "bonjuor" });
        Assert.Null(Corrector(personal: personal).Evaluate("bonjuor", []));
    }

    [Fact]
    public void DigitBearingTokenIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("bonj0ur", []));
    }

    [Fact]
    public void CamelCaseIdentifierIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("onKeyDown", []));
    }

    [Fact]
    public void GibberishWithNoNeighbourIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("zzzzz", []));
    }

    // ── Stub ────────────────────────────────────────────────────────────────

    private sealed class StubPersonal : IPersonalLexicon
    {
        private readonly HashSet<string> _adopted;
        private readonly HashSet<(string, string)> _suppressed;

        public StubPersonal(
            HashSet<string>? adopted = null,
            HashSet<(string original, string replacement)>? suppressed = null)
        {
            _adopted = adopted ?? new();
            _suppressed = suppressed ?? new();
        }

        public bool IsAdopted(string word) => _adopted.Contains(word.ToLowerInvariant());

        public bool IsSuppressed(string original, string replacement) =>
            _suppressed.Contains((original.ToLowerInvariant(), replacement.ToLowerInvariant()));

        public IReadOnlyCollection<string> AdoptedWords => _adopted;
    }
}
