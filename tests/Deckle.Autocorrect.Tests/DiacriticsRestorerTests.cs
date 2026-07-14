using System.Collections.Generic;
using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The lexical gate end to end: the skip chain that protects the literal by
// default and only corrects on unambiguous evidence. Conservativity is the
// product — a wrongly corrected valid word is the worst outcome — so most of
// these assert that a word is LEFT ALONE, and the few corrections are the ones
// that earned it.
[Trait("Category", "unit")]
public class DiacriticsRestorerTests
{
    // The shared French fixture (frequencies per million, Lexique scale).
    // étant/êtant are the synthetic dominance pair: both fold to "etant",
    // 100 vs 1 clears the 20× ratio.
    private const string FrenchTsv =
        "français\t400\nécole\t200\nélève\t30\nélevé\t25\n" +
        "côte\t60\ncote\t50\nà\t9000\na\t10000\n" +
        "aujourd'hui\t80\ndéjà\t150\nmarché\t90\nmarche\t85\ncafé\t120\n" +
        "étant\t100\nêtant\t1\nvoila\t35\nvoilà\t700\n" +
        // Rarity-gate fixture. "mais" (valid, 500) hides maïs (3): ratio 0.006,
        // under the 0.01 floor. "taches" (40) hides tâches (20): ratio 0.5, above.
        // "cotes" (50) hides côtes (40, kept) and cotés (0.1, dropped). "ete" and
        // "ca" are NOT valid literals — their slots exercise the fallback
        // reference (the most frequent lexicon variant): été (500) anchors
        // against êté (1), ça (9000) against çà (20).
        "mais\t500\nmaïs\t3\ntaches\t40\ntâches\t20\n" +
        "cotes\t50\ncôtes\t40\ncotés\t0.1\nété\t500\nêté\t1\n" +
        "ça\t9000\nçà\t20\n";

    private const string EnglishTsv = "the\t60000\nbut\t9000\nmode\t500\ncafe\t0.1\n";

    private static FrequencyLexicon French() => FrequencyLexicon.LoadTsv(new StringReader(FrenchTsv));
    private static FrequencyLexicon English() => FrequencyLexicon.LoadTsv(new StringReader(EnglishTsv));

    private static DiacriticsRestorer Restorer(
        IPairDisambiguator? context = null,
        IPersonalLexicon? personal = null,
        Func<string, IReadOnlyList<AccentVariant>>? personalVariants = null,
        RestorerOptions? options = null,
        bool includeEnglish = true)
    {
        var french = French();
        var index = AccentIndex.Build(french);
        return new DiacriticsRestorer(
            french, includeEnglish ? English() : null, index,
            options, context, personal, personalVariants);
    }

    // ── Single-candidate lexical gate ──────────────────────────────────────

    [Fact]
    public void RestoresUniqueVariant()
    {
        var d = Restorer().Evaluate("francais", []);

        Assert.NotNull(d);
        Assert.Equal("français", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    [Fact]
    public void RestoresAccentOnEcole()
    {
        var d = Restorer().Evaluate("ecole", []);

        Assert.Equal("école", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    [Fact]
    public void PreservesTitleCase()
    {
        var d = Restorer().Evaluate("Ecole", []);

        Assert.Equal("École", d!.Replacement);
    }

    [Fact]
    public void PreservesAllUpperCase()
    {
        var d = Restorer().Evaluate("FRANCAIS", []);

        Assert.Equal("FRANÇAIS", d!.Replacement);
    }

    [Fact]
    public void RestoresDeja()
    {
        // "deja" folds to "deja", whose only accented variant is "déjà" — single.
        var d = Restorer().Evaluate("deja", []);

        Assert.Equal("déjà", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    // ── Literal protection: the guards that leave the word alone ────────────

    [Fact]
    public void ValidFrenchFormIsNeverTouched()
    {
        // "cote" is itself a valid French form — the literal wins (guard 7),
        // even though "côte" exists behind the same fold.
        Assert.Null(Restorer().Evaluate("cote", []));
    }

    [Fact]
    public void MarcheIsProtectedAsValidFormBeforeAmbiguity()
    {
        // "marche" is a valid form (guard 7 fires before any candidate work),
        // although "marché" sits behind the same fold.
        Assert.Null(Restorer().Evaluate("marche", []));
    }

    [Fact]
    public void SingleLetterFormIsLeftAlone()
    {
        // "a" is a valid French form AND below MinWordLength — left alone either way.
        Assert.Null(Restorer().Evaluate("a", []));
    }

    [Fact]
    public void ValidEnglishWordIsNeverFrenchified()
    {
        // No language detection in v1 — the bilingual guard protects a seed
        // English literal even when an accented French candidate exists. Its
        // frequency is deliberately low: membership is the contract.
        Assert.Null(Restorer().Evaluate("cafe", []));
    }

    [Fact]
    public void EnglishShapedTokenIsRestoredWhenTheGlobalEnglishSeedIsAbsent()
    {
        var d = Restorer(includeEnglish: false).Evaluate("cafe", []);

        Assert.NotNull(d);
        Assert.Equal("café", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    [Fact]
    public void PrimaryLexiconCanComeFromTheFrequencyInterface()
    {
        var primary = new StubFrequencyLexicon(new()
        {
            ["cote"] = 50,
        });
        var indexSource = FrequencyLexicon.LoadTsv(new StringReader("côte\t60\n"));
        var restorer = new DiacriticsRestorer(
            primary, english: null, AccentIndex.Build(indexSource));

        Assert.Null(restorer.Evaluate("cote", []));
    }

    [Fact]
    public void DominantVoilaParticleOverridesRareVerbLiteral()
    {
        var d = Restorer().Evaluate("voila", []);

        Assert.NotNull(d);
        Assert.Equal("voilà", d!.Replacement);
        Assert.Equal(CorrectionReason.FrequencyDominance, d.Reason);
    }
    [Fact]
    public void AmbiguousPairWithoutDominanceIsLeftAlone()
    {
        // "eleve" → élève (30) vs élevé (25): ratio 1.2, far under 20, no context.
        Assert.Null(Restorer().Evaluate("eleve", []));
    }

    [Fact]
    public void AlreadyAccentedWordIsNeverSecondGuessed()
    {
        // The user typed the accent deliberately (guard 6).
        Assert.Null(Restorer().Evaluate("déja", []));
    }

    [Fact]
    public void DigitBearingTokenIsBlacklisted()
    {
        Assert.Null(Restorer().Evaluate("win11", []));
    }

    [Fact]
    public void ElisionTokenIsLeftAlone()
    {
        // Trailing apostrophe — an elision prefix ("l'"), not a word.
        Assert.Null(Restorer().Evaluate("l'", []));
    }

    [Fact]
    public void CamelCaseIdentifierIsLeftAlone()
    {
        Assert.Null(Restorer().Evaluate("fooBar", []));
    }

    // ── Frequency dominance ────────────────────────────────────────────────

    [Fact]
    public void DominantVariantWinsWithoutContext()
    {
        // "etant" → étant (100) vs êtant (1): ratio 100, clears 20× and the floor.
        var d = Restorer().Evaluate("etant", []);

        Assert.NotNull(d);
        Assert.Equal("étant", d!.Replacement);
        Assert.Equal(CorrectionReason.FrequencyDominance, d.Reason);
    }

    // ── Context disambiguation ──────────────────────────────────────────────

    [Fact]
    public void ContextResolvesAmbiguousPair()
    {
        // The pair model picks élève; without it "eleve" stays (see above).
        var ctx = new StubDisambiguator(chosen: "élève");
        var d = Restorer(context: ctx).Evaluate("eleve", ["bon"]);

        Assert.NotNull(d);
        Assert.Equal("élève", d!.Replacement);
        Assert.Equal(CorrectionReason.ContextPair, d.Reason);
    }

    [Fact]
    public void ContextReceivesLowercasedPreviousWord()
    {
        var ctx = new StubDisambiguator(chosen: "élève");
        Restorer(context: ctx).Evaluate("eleve", ["Bon"]);

        Assert.Equal("bon", ctx.LastPrevious);
    }

    // ── Personal dictionary ─────────────────────────────────────────────────

    [Fact]
    public void AdoptedWordShieldsItself()
    {
        // The user adopted "ecole" as a literal — it must not be corrected.
        var personal = new StubPersonal(adopted: new() { "ecole" });
        Assert.Null(Restorer(personal: personal).Evaluate("ecole", []));
    }

    [Fact]
    public void SuppressedPairIsBlocked()
    {
        // The user reverted ecole→école once — that pair never fires on its own.
        var personal = new StubPersonal(suppressed: new() { ("ecole", "école") });
        Assert.Null(Restorer(personal: personal).Evaluate("ecole", []));
    }

    [Fact]
    public void PersonalVariantSuppliesACorrection()
    {
        // "lea" is in no lexicon, but the personal dictionary knows "Léa".
        var d = Restorer(personalVariants: lower =>
            lower == "lea"
                ? new[] { new AccentVariant("léa", 0.0) }
                : Array.Empty<AccentVariant>())
            .Evaluate("lea", []);

        Assert.NotNull(d);
        Assert.Equal("léa", d!.Replacement);
        Assert.Equal(CorrectionReason.PersonalWord, d.Reason);
    }

    // ── Miniature end-to-end ────────────────────────────────────────────────

    [Fact]
    public void SentenceCorrectsOnlyTheRestorableWord()
    {
        // « j'ai marche vers l'ecole » — tokenised, only "ecole" is corrected.
        // "j'ai"/"vers" are unknown literals (no variant), "marche" is a valid
        // form, "l'" is an elision token. The gate touches one word.
        var r = Restorer();

        Assert.Null(r.Evaluate("j'ai", []));
        Assert.Null(r.Evaluate("marche", ["j'ai"]));
        Assert.Null(r.Evaluate("vers", ["marche"]));
        Assert.Null(r.Evaluate("l'", ["vers"]));

        var ecole = r.Evaluate("ecole", ["l'"]);
        Assert.NotNull(ecole);
        Assert.Equal("école", ecole!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, ecole.Reason);
    }

    // ── Sentence-stage rarity gate ──────────────────────────────────────────
    //
    // The gate lives in the sentence-stage candidate set, so these exercise
    // SentenceCandidates / AmbiguousCandidates, not Evaluate (the commit stage,
    // which the gate leaves untouched).

    [Fact]
    public void RarityGateDropsAFarRarerVariantWhenTheLiteralIsValid()
    {
        // "cotes" (50) is a valid literal; cotés (0.1) is 500× rarer — ratio
        // 0.002, well under the 0.01 floor — so it is dropped from the slot.
        var candidates = Restorer().SentenceCandidates("cotes", includeTypedLiteral: true);

        Assert.DoesNotContain(candidates, v => v.Form == "cotés");
    }

    [Fact]
    public void RarityGateKeepsAModerateRatioVariant()
    {
        // tâches (20) against the "taches" literal (40) is ratio 0.5 — far above
        // the floor, the class of correct restoration the gate must not touch.
        var candidates = Restorer().SentenceCandidates("taches", includeTypedLiteral: true);

        Assert.Contains(candidates, v => v.Form == "tâches");
    }

    [Fact]
    public void RarityGateFallsBackToTheBestVariantWhenTheLiteralIsNotValid()
    {
        // "ete" is in no lexicon, so the gate's reference falls back to the
        // slot's most frequent variant: été (500) anchors, êté (1) is 500× rarer
        // and drops — collapsing the fold to one form, no longer an ambiguous
        // slot at all (the commit stage restores it deterministically). Before
        // the fallback the gate was disarmed here and êté survived to mislead
        // the judge. With the typed literal included the slot still carries the
        // surviving pair, êté-free.
        Assert.Empty(Restorer().AmbiguousCandidates("ete"));

        var withLiteral = Restorer().SentenceCandidates("ete", includeTypedLiteral: true);
        Assert.Contains(withLiteral, v => v.Form == "été");
        Assert.DoesNotContain(withLiteral, v => v.Form == "êté");
    }

    [Fact]
    public void RarityGateClosesTheCaCedillaHole()
    {
        // The motivating live residue: "ca" is not a valid form, its slot pairs
        // ça (9000) with çà (20) — ratio 0.0022, under the 0.01 floor. The
        // fallback reference drops çà; ça and the typed literal stay.
        var candidates = Restorer().SentenceCandidates("ca", includeTypedLiteral: true);

        Assert.Contains(candidates, v => v.Form == "ça");
        Assert.Contains(candidates, v => v.Form == "ca");
        Assert.DoesNotContain(candidates, v => v.Form == "çà");
    }

    [Fact]
    public void RarityGateFallbackKeepsAModerateRatioPairIntact()
    {
        // "eleve" is not a valid form either, but élève (30) and élevé (25) sit
        // at ratio 0.83 — both are plausible restorations and both must survive
        // the fallback reference.
        var candidates = Restorer().AmbiguousCandidates("eleve");

        Assert.Contains(candidates, v => v.Form == "élève");
        Assert.Contains(candidates, v => v.Form == "élevé");
    }

    [Fact]
    public void RarityGateNeverDropsTheTypedLiteralItself()
    {
        // The literal is the reference, never a casualty: "cotes" stays in the
        // slot even as its far-rarer fold-mate cotés is dropped around it.
        var candidates = Restorer().SentenceCandidates("cotes", includeTypedLiteral: true);

        Assert.Contains(candidates, v => v.Form == "cotes");
        Assert.Contains(candidates, v => v.Form == "côtes");
    }

    [Fact]
    public void RarityGateCollapsingASlotBelowTwoFormsMakesItUnambiguous()
    {
        // "mais" (500) has only maïs (3) behind it, and the gate drops maïs. With
        // just the literal left the fold is no longer a two-way slot at all.
        var restorer = Restorer();

        Assert.Empty(restorer.SentenceCandidates("mais", includeTypedLiteral: true));
        Assert.Empty(restorer.AmbiguousCandidates("mais"));
    }

    // ── Stubs ───────────────────────────────────────────────────────────────

    private sealed class StubDisambiguator(string? chosen) : IPairDisambiguator
    {
        public string? LastPrevious { get; private set; }

        public string? Choose(IReadOnlyList<string> leftContext, IReadOnlyList<AccentVariant> candidates, StageTrace? trace = null)
        {
            LastPrevious = leftContext.Count > 0 ? leftContext[^1] : null;
            return chosen;
        }
    }

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
