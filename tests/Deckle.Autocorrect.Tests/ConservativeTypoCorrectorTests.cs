using System.Collections.Generic;
using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The typo corrector acts only on NON-words, and only when a common French word
// sits one edit away (two in the far tier) and clearly dominates. Edits
// modelled: deletion, transposition, keyboard-adjacent substitution, insertion.
public class ConservativeTypoCorrectorTests
{
    // Frequencies per million, Lexique scale. ballet/billet are the synthetic
    // ambiguity pair (both one insertion from "bllet", equal frequency); monde
    // pairs with the English "mode" for the bilingual guard.
    private const string FrenchTsv =
        "bonjour\t300\nballet\t100\nbillet\t100\nobscur\t2\nchat\t90\nmonde\t100\n";

    private const string EnglishTsv = "the\t60000\nmode\t0.1\n";

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
    [Trait("Category", "unit")]
    public void CorrectsTransposition()
    {
        var d = Corrector().Evaluate("bonjuor", []);

        Assert.NotNull(d);
        Assert.Equal("bonjour", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ComposesATranspositionWithMissingDiacritics()
    {
        FrequencyLexicon french = FrequencyLexicon.LoadTsv(new StringReader(
            "préparer\t300\npréparé\t20\n"));
        var corrector = new ConservativeTypoCorrector(
            french, accentIndex: AccentIndex.Build(french));

        CorrectionDecision? decision = corrector.Evaluate("preaprer", []);

        Assert.NotNull(decision);
        Assert.Equal("préparer", decision!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, decision.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void CorrectsMissingLetter()
    {
        var d = Corrector().Evaluate("bonjor", []);

        Assert.Equal("bonjour", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void CorrectsAdjacentSubstitution()
    {
        var d = Corrector().Evaluate("bobjour", []);

        Assert.Equal("bonjour", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PreservesSentenceInitialCase()
    {
        // No left context means sentence-initial, so the capital is not a name.
        var d = Corrector().Evaluate("Bonjuor", []);

        Assert.Equal("Bonjour", d!.Replacement);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void DominantNeighbourWins()
    {
        // ballet 2000 vs billet 100: ratio 20 clears the 5× near bar.
        var d = Corrector("ballet\t2000\nbillet\t100\n").Evaluate("bllet", []);

        Assert.Equal("ballet", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    // ── Conservativity: the cases left alone ────────────────────────────────

    [Fact]
    [Trait("Category", "unit")]
    public void ValidFrenchWordIsNeverTouched()
    {
        Assert.Null(Corrector().Evaluate("chat", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void NonAdjacentSubstitutionOnAShortWordIsNotAPlausibleSlip()
    {
        // "zhat" → "chat" needs z→c, but z and c do not touch on QWERTY, so it is
        // no near candidate; and at four letters the word is below the far tier's
        // length floor, so the two-edit path cannot rescue it either.
        Assert.Null(Corrector().Evaluate("zhat", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AmbiguousNeighboursAreLeftAlone()
    {
        // "bllet" is one insertion from both ballet (100) and billet (100):
        // ratio 1, under the 5× near bar.
        Assert.Null(Corrector().Evaluate("bllet", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AmbiguousOneKeyNeighboursBecomeAClosedSentenceCandidateSet()
    {
        var corrector = Corrector("peu\t1586.96\npu\t366.22\npur\t44.59\n");
        Assert.Null(corrector.Evaluate("pru", [])); // 4.33× stays below the 5× commit bar.

        IReadOnlyList<AccentVariant> candidates = corrector.AmbiguousCandidates("pru");

        Assert.Equal(new[] { "peu", "pu", "pur", "pru" },
            candidates.Select(candidate => candidate.Form).ToArray());
    }

    [Fact]
    [Trait("Category", "unit")]
    public void CoherentHorizontalHandOffsetIsAContextualCandidateOnly()
    {
        var corrector = Corrector("qui\t9000\n");

        Assert.Null(corrector.Evaluate("qio", []));
        Assert.Contains(
            corrector.AmbiguousCandidates("qio"),
            candidate => candidate.Form == "qui");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ShortTwoSlipFaultIsAContextualCandidateOnly()
    {
        var corrector = Corrector("mieux\t900\n");

        Assert.Null(corrector.Evaluate("miru", []));
        Assert.Contains(
            corrector.AmbiguousCandidates("miru"),
            candidate => candidate.Form == "mieux");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RareNeighbourBelowFloorIsLeftAlone()
    {
        // "obscru" → obscur, but obscur (1.5) sits under the 2/million floor:
        // not common enough to be the obvious intent.
        Assert.Null(Corrector("obscur\t1.5\n").Evaluate("obscru", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ShortWordIsLeftAlone()
    {
        // "ch" is below MinWordLength, too little signal, even though "chat" is
        // two edits away.
        Assert.Null(Corrector().Evaluate("ch", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ProperNounMidSentenceIsLeftAlone()
    {
        // A capitalised token mid-utterance is a name, not a typo to fix.
        Assert.Null(Corrector().Evaluate("Bonjuor", ["salut"]));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void EnglishShapedTokenIsCorrectedWithoutTheGlobalEnglishSeed()
    {
        var d = Corrector().Evaluate("mode", []);

        Assert.NotNull(d);
        Assert.Equal("monde", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ValidEnglishWordIsNotFrenchified()
    {
        // The bilingual guard works by membership, whatever the English frequency.
        var english = FrequencyLexicon.LoadTsv(new StringReader(EnglishTsv));
        Assert.Null(Corrector(english: english).Evaluate("mode", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PrimaryLexiconCanComeFromTheFrequencyInterface()
    {
        var french = new StubFrequencyLexicon(new()
        {
            ["bonjour"] = 300,
        });

        var d = new ConservativeTypoCorrector(french).Evaluate("bonjuor", []);

        Assert.NotNull(d);
        Assert.Equal("bonjour", d!.Replacement);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AdoptedWordShieldsItself()
    {
        var personal = new StubPersonal(adopted: new() { "bonjuor" });
        Assert.Null(Corrector(personal: personal).Evaluate("bonjuor", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void DigitBearingTokenIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("bonj0ur", []));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void PackagedLexiconPrefersTheDeduplicatedFrenchVerb()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        var corrector = new ConservativeTypoCorrector(
            french,
            accentIndex: AccentIndex.Build(french),
            verbs: VerbMorphology.LoadTsvGz(Path.Combine(
                dataDir, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName)));

        var trace = new CorrectionTrace();
        CorrectionDecision? decision = corrector.Evaluate("proposees", ["tu"], trace);

        Assert.True(
            decision is not null,
            $"{trace.RenderTrail()} | {trace.RenderCandidates()} | {trace.RenderGauges()}");
        Assert.Equal("proposes", decision!.Replacement);
    }

    [Theory]
    [Trait("Category", "integration")]
    [InlineData("pru", "peu")]
    [InlineData("qio", "qui")]
    [InlineData("miru", "mieux")]
    public void PackagedSentenceCandidatesKeepObservedKeyboardRepairs(
        string typed,
        string expected)
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        var corrector = new ConservativeTypoCorrector(
            french, accentIndex: AccentIndex.Build(french));

        IReadOnlyList<AccentVariant> candidates = corrector.AmbiguousCandidates(typed);

        Assert.True(
            candidates.Any(candidate => candidate.Form == expected),
            string.Join(", ", candidates.Select(candidate =>
                $"{candidate.Form}@{candidate.FrequencyPerMillion}")));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void CamelCaseIdentifierIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("onKeyDown", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void GibberishWithNoNeighbourIsLeftAlone()
    {
        Assert.Null(Corrector().Evaluate("zzzzz", []));
    }

    // ── The far tier: two edits, stricter bar ───────────────────────────────

    [Fact]
    [Trait("Category", "unit")]
    public void CorrectsTwoEditFault()
    {
        var d = Corrector().Evaluate("bonjuro", []);

        Assert.Equal("bonjour", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void TwoEditFaultOnShortWordIsLeftAlone()
    {
        // "xonde" is two edits from "monde" (delete x, insert m), but at five
        // letters it is below the far tier's length floor — left untouched.
        Assert.Null(Corrector().Evaluate("xonde", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RelaxedDominanceResolvesAModerateRatio()
    {
        // ballet 600 vs billet 100: ratio 6 clears the 5× near bar.
        var d = Corrector("ballet\t600\nbillet\t100\n").Evaluate("bllet", []);

        Assert.Equal("ballet", d!.Replacement);
        Assert.Equal(CorrectionReason.TypoCorrection, d.Reason);
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

    private sealed class StubFrequencyLexicon(Dictionary<string, double> entries) : IFrequencyLexicon
    {
        public bool Contains(string lowerForm) => entries.ContainsKey(lowerForm);

        public double FrequencyOf(string lowerForm) =>
            entries.TryGetValue(lowerForm, out double frequency) ? frequency : 0.0;
    }
}
