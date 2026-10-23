using System.Collections.Generic;
using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The lexical gate end to end: the skip chain that protects the literal by
// default and only corrects on unambiguous evidence. Conservativity is the
// product — a wrongly corrected valid word is the worst outcome — so most of
// these assert that a word is LEFT ALONE.
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
    [Trait("Category", "unit")]
    public void RestoresUniqueVariant()
    {
        var d = Restorer().Evaluate("francais", []);

        Assert.NotNull(d);
        Assert.Equal("français", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RestoresAccentOnEcole()
    {
        var d = Restorer().Evaluate("ecole", []);

        Assert.Equal("école", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PreservesTitleCase()
    {
        var d = Restorer().Evaluate("Ecole", []);

        Assert.Equal("École", d!.Replacement);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PreservesAllUpperCase()
    {
        var d = Restorer().Evaluate("FRANCAIS", []);

        Assert.Equal("FRANÇAIS", d!.Replacement);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RestoresDeja()
    {
        var d = Restorer().Evaluate("deja", []);

        Assert.Equal("déjà", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    // ── Literal protection: the guards that leave the word alone ────────────

    [Fact]
    [Trait("Category", "unit")]
    public void ValidFrenchFormIsNeverTouched()
    {
        Assert.Null(Restorer().Evaluate("cote", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void MarcheIsProtectedAsValidFormBeforeAmbiguity()
    {
        Assert.Null(Restorer().Evaluate("marche", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void SingleLetterFormIsLeftAlone()
    {
        // "a" is a valid French form AND below MinWordLength — left alone either way.
        Assert.Null(Restorer().Evaluate("a", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ValidEnglishWordIsNeverFrenchified()
    {
        // The bilingual guard protects a seed English literal even when an
        // accented French candidate exists. Its frequency is deliberately low:
        // membership is the contract.
        Assert.Null(Restorer().Evaluate("cafe", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void EnglishShapedTokenIsRestoredWhenTheGlobalEnglishSeedIsAbsent()
    {
        var d = Restorer(includeEnglish: false).Evaluate("cafe", []);

        Assert.NotNull(d);
        Assert.Equal("café", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
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
    [Trait("Category", "unit")]
    public void DominantVoilaParticleOverridesRareVerbLiteral()
    {
        var d = Restorer().Evaluate("voila", []);

        Assert.NotNull(d);
        Assert.Equal("voilà", d!.Replacement);
        Assert.Equal(CorrectionReason.FrequencyDominance, d.Reason);
    }
    [Fact]
    [Trait("Category", "unit")]
    public void AmbiguousPairWithoutDominanceIsLeftAlone()
    {
        // "eleve" → élève (30) vs élevé (25): ratio 1.2, far under 20, no context.
        Assert.Null(Restorer().Evaluate("eleve", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void AlreadyAccentedWordIsNeverSecondGuessed()
    {
        // The user typed the accent deliberately.
        Assert.Null(Restorer().Evaluate("déja", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void DigitBearingTokenIsBlacklisted()
    {
        Assert.Null(Restorer().Evaluate("win11", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ElisionTokenIsLeftAlone()
    {
        Assert.Null(Restorer().Evaluate("l'", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void CamelCaseIdentifierIsLeftAlone()
    {
        Assert.Null(Restorer().Evaluate("fooBar", []));
    }

    // ── Frequency dominance ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "unit")]
    public void DominantVariantWinsWithoutContext()
    {
        var d = Restorer().Evaluate("etant", []);

        Assert.NotNull(d);
        Assert.Equal("étant", d!.Replacement);
        Assert.Equal(CorrectionReason.FrequencyDominance, d.Reason);
    }

    // ── Context disambiguation ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "unit")]
    public void ContextResolvesAmbiguousPair()
    {
        var ctx = new StubDisambiguator(chosen: "élève");
        var d = Restorer(context: ctx).Evaluate("eleve", ["bon"]);

        Assert.NotNull(d);
        Assert.Equal("élève", d!.Replacement);
        Assert.Equal(CorrectionReason.ContextPair, d.Reason);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void ContextReceivesLowercasedPreviousWord()
    {
        var ctx = new StubDisambiguator(chosen: "élève");
        Restorer(context: ctx).Evaluate("eleve", ["Bon"]);

        Assert.Equal("bon", ctx.LastPrevious);
    }

    // ── Personal dictionary ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "unit")]
    public void AdoptedWordShieldsItself()
    {
        var personal = new StubPersonal(adopted: new() { "ecole" });
        Assert.Null(Restorer(personal: personal).Evaluate("ecole", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void SuppressedPairIsBlocked()
    {
        var personal = new StubPersonal(suppressed: new() { ("ecole", "école") });
        Assert.Null(Restorer(personal: personal).Evaluate("ecole", []));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PersonalVariantSuppliesACorrection()
    {
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
    [Trait("Category", "unit")]
    public void SentenceCorrectsOnlyTheRestorableWord()
    {
        // « j'ai marche vers l'ecole »: "j'ai"/"vers" are unknown literals (no
        // variant), "marche" is a valid form, "l'" is an elision token.
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
    [Trait("Category", "unit")]
    public void RarityGateDropsAFarRarerVariantWhenTheLiteralIsValid()
    {
        var candidates = Restorer().SentenceCandidates("cotes", includeTypedLiteral: true);

        Assert.DoesNotContain(candidates, v => v.Form == "cotés");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RarityGateKeepsAModerateRatioVariant()
    {
        var candidates = Restorer().SentenceCandidates("taches", includeTypedLiteral: true);

        Assert.Contains(candidates, v => v.Form == "tâches");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RarityGateFallsBackToTheBestVariantWhenTheLiteralIsNotValid()
    {
        // Once êté drops, the fold collapses to one form and is no longer an
        // ambiguous slot; the commit stage restores it deterministically.
        Assert.Empty(Restorer().AmbiguousCandidates("ete"));

        var withLiteral = Restorer().SentenceCandidates("ete", includeTypedLiteral: true);
        Assert.Contains(withLiteral, v => v.Form == "été");
        Assert.DoesNotContain(withLiteral, v => v.Form == "êté");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RarityGateClosesTheCaCedillaHole()
    {
        var candidates = Restorer().SentenceCandidates("ca", includeTypedLiteral: true);

        Assert.Contains(candidates, v => v.Form == "ça");
        Assert.Contains(candidates, v => v.Form == "ca");
        Assert.DoesNotContain(candidates, v => v.Form == "çà");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RarityGateFallbackKeepsAModerateRatioPairIntact()
    {
        var candidates = Restorer().AmbiguousCandidates("eleve");

        Assert.Contains(candidates, v => v.Form == "élève");
        Assert.Contains(candidates, v => v.Form == "élevé");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RarityGateNeverDropsTheTypedLiteralItself()
    {
        var candidates = Restorer().SentenceCandidates("cotes", includeTypedLiteral: true);

        Assert.Contains(candidates, v => v.Form == "cotes");
        Assert.Contains(candidates, v => v.Form == "côtes");
    }

    [Fact]
    [Trait("Category", "unit")]
    public void RarityGateCollapsingASlotBelowTwoFormsMakesItUnambiguous()
    {
        var restorer = Restorer();

        Assert.Empty(restorer.SentenceCandidates("mais", includeTypedLiteral: true));
        Assert.Empty(restorer.AmbiguousCandidates("mais"));
    }

    [Theory]
    [Trait("Category", "unit")]
    [InlineData("ratures")]
    [InlineData("date")]
    public void InflectedContentWordsDoNotBecomeSilentSentenceHomophoneSlots(string literal)
    {
        var french = FrequencyLexicon.LoadTsv(new StringReader(
            "ratures\t10\nraturés\t2\ndate\t10\ndaté\t2\n"));
        var restorer = new DiacriticsRestorer(french, null, AccentIndex.Build(french));

        Assert.Empty(restorer.AmbiguousCandidates(literal));
    }

    [Theory]
    [Trait("Category", "unit")]
    [InlineData("a", "à")]
    [InlineData("la", "là")]
    [InlineData("ou", "où")]
    [InlineData("du", "dû")]
    [InlineData("sur", "sûr")]
    public void CalibratedFrenchHomophonesRemainSentenceSlots(string literal, string accented)
    {
        var french = FrequencyLexicon.LoadTsv(new StringReader(
            $"{literal}\t10\n{accented}\t5\n"));
        var restorer = new DiacriticsRestorer(french, null, AccentIndex.Build(french));

        IReadOnlyList<AccentVariant> candidates = restorer.AmbiguousCandidates(literal);

        Assert.Contains(candidates, candidate => candidate.Form == literal);
        Assert.Contains(candidates, candidate => candidate.Form == accented);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void PackagedMorphologyRestoresPrepareAfterJe()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        AutocorrectPolicySet policies = AutocorrectPolicySet.Create(
            french,
            english: null,
            AccentIndex.Build(french),
            context: BigramPairDisambiguator.LoadTsvGz(Path.Combine(
                dataDir, AutocorrectLexiconArtifacts.PairBigramsFrenchFileName)),
            verbs: VerbMorphology.LoadTsvGz(Path.Combine(
                dataDir, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName)));
        var trace = new CorrectionTrace();

        CorrectionDecision? decision = policies.Policy.Evaluate("prepare", ["je"], trace);

        Assert.True(
            decision is not null,
            $"{trace.RenderTrail()} | {trace.RenderCandidates()} | {trace.RenderGauges()}");
        Assert.True(
            decision!.Replacement == "prépare",
            $"{trace.RenderTrail()} | {trace.RenderCandidates()} | {trace.RenderGauges()}");
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
