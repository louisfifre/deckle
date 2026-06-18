using System.Collections.Generic;
using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The elision corrector: restore a dropped apostrophe in a glued proclitic
// ("cest" → "c'est"). As with the other stages, conservativity is the product —
// the load-bearing guard is that a glued elision is never itself a valid word,
// so the bulk of these assert a real word is LEFT ALONE while the few splits are
// the ones that earned it.
[Trait("Category", "unit")]
public class ElisionCorrectorTests
{
    // Tails that license an elision (est, ai, il, un, en, on, arrache, homme,
    // eau, église, a) plus the valid words that look like glued elisions but are
    // not (dune, quelle, tas, ces, sur, bonjour).
    private const string FrenchTsv =
        "est\t1000\nai\t800\nil\t900\nun\t900\nen\t900\non\t900\narrache\t40\n" +
        "homme\t300\neau\t200\néglise\t60\na\t2000\n" +
        "dune\t20\nquelle\t150\ntas\t60\nces\t400\nsur\t500\nbonjour\t300\n";

    private static ElisionCorrector Corrector(
        string? frenchTsv = null, IPersonalLexicon? personal = null)
    {
        var french = FrequencyLexicon.LoadTsv(new StringReader(frenchTsv ?? FrenchTsv));
        return new ElisionCorrector(french, personal);
    }

    // ── Splits that earned it ───────────────────────────────────────────────

    [Theory]
    [InlineData("cest", "c'est")]
    [InlineData("jai", "j'ai")]
    [InlineData("quil", "qu'il")]
    [InlineData("quon", "qu'on")]
    [InlineData("dun", "d'un")]
    [InlineData("larrache", "l'arrache")]
    [InlineData("lhomme", "l'homme")]   // mute h licenses the elision
    [InlineData("leau", "l'eau")]
    [InlineData("nest", "n'est")]
    [InlineData("sil", "s'il")]
    [InlineData("léglise", "l'église")] // accented tail kept, apostrophe restored
    public void RestoresDroppedElision(string typed, string expected)
    {
        var d = Corrector().Evaluate(typed, []);

        Assert.NotNull(d);
        Assert.Equal(expected, d!.Replacement);
        Assert.Equal(CorrectionReason.Elision, d.Reason);
    }

    [Fact]
    public void CarriesSentenceInitialCapital()
    {
        // No left context: a capitalised glued elision is split and the capital
        // carried onto the proclitic head ("Cest" → "C'est").
        var d = Corrector().Evaluate("Cest", []);

        Assert.Equal("C'est", d!.Replacement);
        Assert.Equal(CorrectionReason.Elision, d.Reason);
    }

    // ── Conservativity: the cases left alone ────────────────────────────────

    [Theory]
    [InlineData("dune")]    // a real word, not d'une
    [InlineData("quelle")]  // a real word, not qu'elle
    [InlineData("tas")]     // a real word, not t'as
    [InlineData("ces")]     // a real word, not c'es
    [InlineData("sur")]     // a real word
    public void ValidFrenchWordIsNeverSplit(string word)
    {
        Assert.Null(Corrector().Evaluate(word, []));
    }

    [Fact]
    public void TwoLetterTailFloorLeavesCedillaWordForTheGate()
    {
        // "ca" would split to "c'a", but the user means "ça" (a cedilla, another
        // stage's job). The two-letter-tail floor leaves it untouched.
        Assert.Null(Corrector().Evaluate("ca", []));
    }

    [Fact]
    public void ProperNounMidSentenceIsLeftAlone()
    {
        // A capitalised token mid-utterance is a name, not a dropped elision.
        Assert.Null(Corrector().Evaluate("Cest", ["bonjour"]));
    }

    [Fact]
    public void TailThatIsNotAWordIsLeftAlone()
    {
        // "lxyz": "l" + "xyz", but "xyz" is no French word — not an elision.
        Assert.Null(Corrector().Evaluate("lxyz", []));
    }

    [Fact]
    public void AlreadyApostrophedFormIsLeftAlone()
    {
        // The apostrophe is already there; a non-letter takes it out of scope.
        Assert.Null(Corrector().Evaluate("c'est", []));
    }

    [Fact]
    public void NonProcliticLeadingLetterIsLeftAlone()
    {
        // "best" starts with no proclitic — "b" does not elide — so it is left
        // for the typo corrector, not split.
        Assert.Null(Corrector().Evaluate("best", []));
    }

    [Fact]
    public void AdoptedWordShieldsItself()
    {
        // The user adopted the glued form as their own — never split it.
        var personal = new StubPersonal(adopted: new() { "cest" });
        Assert.Null(Corrector(personal: personal).Evaluate("cest", []));
    }

    [Fact]
    public void CamelCaseIdentifierIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("nOnWord", []));
    }

    [Fact]
    public void DigitBearingTokenIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("c3st", []));
    }

    // ── Stub ────────────────────────────────────────────────────────────────

    private sealed class StubPersonal : IPersonalLexicon
    {
        private readonly HashSet<string> _adopted;

        public StubPersonal(HashSet<string>? adopted = null) => _adopted = adopted ?? new();

        public bool IsAdopted(string word) => _adopted.Contains(word.ToLowerInvariant());

        public bool IsSuppressed(string original, string replacement) => false;

        public IReadOnlyCollection<string> AdoptedWords => _adopted;
    }
}
