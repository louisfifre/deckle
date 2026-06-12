using System.Collections.Generic;
using System.IO;
using Deckle.Input.Autocorrect.Engine;
using Deckle.Input.Autocorrect.Learning;
using Deckle.Input.Autocorrect.Lexicon;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

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
        "aujourd'hui\t80\ndéjà\t150\nmarché\t90\nmarche\t85\n" +
        "étant\t100\nêtant\t1\n";

    private const string EnglishTsv = "the\t60000\nbut\t9000\nmode\t500\n";

    private static FrequencyLexicon French() => FrequencyLexicon.LoadTsv(new StringReader(FrenchTsv));
    private static FrequencyLexicon English() => FrequencyLexicon.LoadTsv(new StringReader(EnglishTsv));

    private static DiacriticsRestorer Restorer(
        IPairDisambiguator? context = null,
        IPersonalLexicon? personal = null,
        Func<string, IReadOnlyList<AccentVariant>>? personalVariants = null,
        RestorerOptions? options = null)
    {
        var french = French();
        var index = AccentIndex.Build(french);
        return new DiacriticsRestorer(french, English(), index, options, context, personal, personalVariants);
    }

    // ── Single-candidate lexical gate ──────────────────────────────────────

    [Fact]
    public void RestoresUniqueVariant()
    {
        var d = Restorer().Evaluate("francais", previousWord: null);

        Assert.NotNull(d);
        Assert.Equal("français", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    [Fact]
    public void RestoresAccentOnEcole()
    {
        var d = Restorer().Evaluate("ecole", null);

        Assert.Equal("école", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    [Fact]
    public void PreservesTitleCase()
    {
        var d = Restorer().Evaluate("Ecole", null);

        Assert.Equal("École", d!.Replacement);
    }

    [Fact]
    public void PreservesAllUpperCase()
    {
        var d = Restorer().Evaluate("FRANCAIS", null);

        Assert.Equal("FRANÇAIS", d!.Replacement);
    }

    [Fact]
    public void RestoresDeja()
    {
        // "deja" folds to "deja", whose only accented variant is "déjà" — single.
        var d = Restorer().Evaluate("deja", null);

        Assert.Equal("déjà", d!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, d.Reason);
    }

    // ── Literal protection: the guards that leave the word alone ────────────

    [Fact]
    public void ValidFrenchFormIsNeverTouched()
    {
        // "cote" is itself a valid French form — the literal wins (guard 7),
        // even though "côte" exists behind the same fold.
        Assert.Null(Restorer().Evaluate("cote", null));
    }

    [Fact]
    public void MarcheIsProtectedAsValidFormBeforeAmbiguity()
    {
        // "marche" is a valid form (guard 7 fires before any candidate work),
        // although "marché" sits behind the same fold.
        Assert.Null(Restorer().Evaluate("marche", null));
    }

    [Fact]
    public void SingleLetterFormIsLeftAlone()
    {
        // "a" is a valid French form AND below MinWordLength — left alone either way.
        Assert.Null(Restorer().Evaluate("a", null));
    }

    [Fact]
    public void EnglishWordIsNeverFrenchified()
    {
        // No language detection in v1 — the bilingual guard protects "the".
        Assert.Null(Restorer().Evaluate("the", null));
    }

    [Fact]
    public void AmbiguousPairWithoutDominanceIsLeftAlone()
    {
        // "eleve" → élève (30) vs élevé (25): ratio 1.2, far under 20, no context.
        Assert.Null(Restorer().Evaluate("eleve", null));
    }

    [Fact]
    public void AlreadyAccentedWordIsNeverSecondGuessed()
    {
        // The user typed the accent deliberately (guard 6).
        Assert.Null(Restorer().Evaluate("déja", null));
    }

    [Fact]
    public void DigitBearingTokenIsBlacklisted()
    {
        Assert.Null(Restorer().Evaluate("win11", null));
    }

    [Fact]
    public void ElisionTokenIsLeftAlone()
    {
        // Trailing apostrophe — an elision prefix ("l'"), not a word.
        Assert.Null(Restorer().Evaluate("l'", null));
    }

    [Fact]
    public void CamelCaseIdentifierIsLeftAlone()
    {
        Assert.Null(Restorer().Evaluate("fooBar", null));
    }

    // ── Frequency dominance ────────────────────────────────────────────────

    [Fact]
    public void DominantVariantWinsWithoutContext()
    {
        // "etant" → étant (100) vs êtant (1): ratio 100, clears 20× and the floor.
        var d = Restorer().Evaluate("etant", null);

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
        var d = Restorer(context: ctx).Evaluate("eleve", previousWord: "bon");

        Assert.NotNull(d);
        Assert.Equal("élève", d!.Replacement);
        Assert.Equal(CorrectionReason.ContextPair, d.Reason);
    }

    [Fact]
    public void ContextReceivesLowercasedPreviousWord()
    {
        var ctx = new StubDisambiguator(chosen: "élève");
        Restorer(context: ctx).Evaluate("eleve", previousWord: "Bon");

        Assert.Equal("bon", ctx.LastPrevious);
    }

    // ── Personal dictionary ─────────────────────────────────────────────────

    [Fact]
    public void AdoptedWordShieldsItself()
    {
        // The user adopted "ecole" as a literal — it must not be corrected.
        var personal = new StubPersonal(adopted: new() { "ecole" });
        Assert.Null(Restorer(personal: personal).Evaluate("ecole", null));
    }

    [Fact]
    public void SuppressedPairIsBlocked()
    {
        // The user reverted ecole→école once — that pair never fires on its own.
        var personal = new StubPersonal(suppressed: new() { ("ecole", "école") });
        Assert.Null(Restorer(personal: personal).Evaluate("ecole", null));
    }

    [Fact]
    public void PersonalVariantSuppliesACorrection()
    {
        // "lea" is in no lexicon, but the personal dictionary knows "Léa".
        var d = Restorer(personalVariants: lower =>
            lower == "lea"
                ? new[] { new AccentVariant("léa", 0.0) }
                : Array.Empty<AccentVariant>())
            .Evaluate("lea", null);

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

        Assert.Null(r.Evaluate("j'ai", null));
        Assert.Null(r.Evaluate("marche", "j'ai"));
        Assert.Null(r.Evaluate("vers", "marche"));
        Assert.Null(r.Evaluate("l'", "vers"));

        var ecole = r.Evaluate("ecole", "l'");
        Assert.NotNull(ecole);
        Assert.Equal("école", ecole!.Replacement);
        Assert.Equal(CorrectionReason.LexicalGate, ecole.Reason);
    }

    // ── Stubs ───────────────────────────────────────────────────────────────

    private sealed class StubDisambiguator(string? chosen) : IPairDisambiguator
    {
        public string? LastPrevious { get; private set; }

        public string? Choose(string? previousWord, IReadOnlyList<AccentVariant> candidates)
        {
            LastPrevious = previousWord;
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
}
