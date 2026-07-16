using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The mistouch miner — the offline discovery behind the family artifact. These
// pin the boundaries that make a family trustworthy: only single mechanical
// edits classify (diacritics fixes and rewordings never do), residue repairs
// stay bounded to the known hypotheses and flag ambiguity instead of choosing,
// and evidence aggregates by signature with its distinct-days recurrence.
[Trait("Category", "unit")]
public class MistouchMinerTests
{
    // A miniature lexicon standing in for the tiers; the miner only asks
    // « is this a word » so a set is a faithful stand-in.
    private sealed class SetLexicon : IFrequencyLexicon
    {
        private readonly HashSet<string> _words;
        public SetLexicon(params string[] words) => _words = new HashSet<string>(words);
        public bool Contains(string lowerForm) => _words.Contains(lowerForm);
        public double FrequencyOf(string lowerForm) => _words.Contains(lowerForm) ? 1 : 0;
    }

    private static readonly IFrequencyLexicon French = new SetLexicon(
        "il", "fait", "beau", "chien", "chat", "bonjour", "monde", "de", "la");

    private static CorpusEntry Entry(
        string final, string history = "", string typed = "", string day = "2026-07-14")
        => new(new SentenceCorpus.SentenceRecord(
            typed.Length > 0 ? typed : final, final, history), true, "test.exe", day);

    private static MistouchMiner.MiningResult Mine(params CorpusEntry[] entries)
        => MistouchMiner.Mine(entries, French, english: null);

    // ── Single-edit classification ───────────────────────────────────────

    [Theory]
    [InlineData("chuen", "chien", "sub u→i", "substitution")]
    [InlineData("chein", "chien", "transposed ei", "transposition")]
    [InlineData("chhien", "chien", "doubled h", "doubling")]
    [InlineData("chien", "chiens", "dropped s after n", "omission")]
    public void ClassifiesSingleMechanicalEdits(string from, string to, string signature, string kind)
    {
        var classified = MistouchMiner.ClassifySingleEdit(from, to);
        Assert.NotNull(classified);
        Assert.Equal(signature, classified.Value.Signature);
        Assert.Equal(kind, classified.Value.Kind);
    }

    [Theory]
    [InlineData("ecole", "école")]   // diacritics — the restorer's domain
    [InlineData("chien", "cheval")]  // rewording — wider than one edit
    [InlineData("mot", "mot")]       // no edit at all
    public void RefusesWhatIsNotOneMechanicalEdit(string from, string to)
    {
        Assert.Null(MistouchMiner.ClassifySingleEdit(from, to));
    }

    // ── Repaired lane ────────────────────────────────────────────────────

    [Fact]
    public void MinesUserRepairsFromTheHistory()
    {
        var result = Mine(Entry("il fait beau.", history: "#1=fqit»user:fait"));

        var family = Assert.Single(result.Families, f => f.Signature == "sub q→a");
        Assert.Equal(1, family.RepairedCount);
        Assert.True(family.Examples[0].Repaired);
    }

    [Fact]
    public void ReadsTheFormBeforeEachUserTransitionNotTheFirstTyped()
    {
        // commit repaired first, THEN the user re-edited: the user's evidence
        // pair starts from the commit's output, not from the raw first-typed.
        var result = Mine(Entry("il fait beau.", history: "#1=fqit»commit:fait»user:faim"));

        Assert.DoesNotContain(result.Families, f => f.Signature == "sub q→a");
        Assert.Contains(result.Families, f => f.Signature == "sub t→m");
    }

    [Fact]
    public void KeepsUnreadableRepairsVisibleAsUnclassified()
    {
        var result = Mine(Entry("la chat.", history: "#1=bonjour»user:chat"));

        var pair = Assert.Single(result.Unclassified);
        Assert.Equal(("bonjour", "chat"), (pair.From, pair.To));
    }

    // ── Residue lane ─────────────────────────────────────────────────────

    [Fact]
    public void MinesUnfixedNonWordsTheAdjacencyRepairReads()
    {
        // « chuen » stands unfixed in the final text; u→i touches on QWERTY
        // and lands on a word — residue evidence, unambiguous.
        var result = Mine(Entry("le chuen dort."));

        var family = Assert.Single(result.Families, f => f.Signature == "sub u→i");
        Assert.Equal(1, family.ResidueCount);
        Assert.Equal(0, family.AmbiguousCount);
    }

    [Fact]
    public void FlagsResidueSeveralHypothesesCanRepair()
    {
        // « eort » : e touches both d and f on QWERTY, and dort AND fort are
        // words — two readings, so each signature carries the ambiguity flag
        // instead of the miner choosing (the sentence-stage routing case).
        var result = MistouchMiner.Mine(
            new[] { Entry("il eort bien.") }, new SetLexicon("dort", "fort", "il", "bien"),
            english: null);

        var toDort = Assert.Single(result.Families, f => f.Signature == "sub e→d");
        var toFort = Assert.Single(result.Families, f => f.Signature == "sub e→f");
        Assert.Equal(1, toDort.AmbiguousCount);
        Assert.Equal(1, toFort.AmbiguousCount);
    }

    [Fact]
    public void MinesTheSemicolonForApostropheAtTheBoundary()
    {
        var result = Mine(Entry("qu;il fait beau."));

        var family = Assert.Single(result.Families, f => f.Signature == "sub ;→'");
        Assert.Equal("qu;il", family.Examples[0].From);
        Assert.Equal("qu'il", family.Examples[0].To);
    }

    [Fact]
    public void MinesTheDroppedSpaceOnlyBetweenTwoWords()
    {
        var result = Mine(
            Entry("il fait,beau dehors."),   // two words glued — evidence
            Entry("voir app.jsonl ici."));   // an identifier — never evidence

        var family = Assert.Single(result.Families, f => f.Signature == "dropped space after ,");
        Assert.Equal(1, family.Evidence);
        Assert.DoesNotContain(result.Families, f => f.Signature == "dropped space after .");
    }

    [Fact]
    public void ResidueSkipsValidWordsAndNonAsciiTokens()
    {
        // « chat » is a word; « fenêtre » carries a diacritic (the restorer's
        // domain, not the miner's) — neither is scanned.
        var result = Mine(Entry("le chat fenêtre."));

        Assert.Empty(result.Families);
    }

    // ── Aggregation ──────────────────────────────────────────────────────

    [Fact]
    public void AggregatesEvidenceBySignatureAcrossLanesAndDays()
    {
        var result = Mine(
            Entry("il fait beau.", history: "#1=fqit»user:fait", day: "2026-07-13"),
            Entry("le chqt dort.", day: "2026-07-14"));

        var family = Assert.Single(result.Families, f => f.Signature == "sub q→a");
        Assert.Equal(2, family.Evidence);
        Assert.Equal(1, family.RepairedCount);
        Assert.Equal(1, family.ResidueCount);
        Assert.Equal(2, family.DistinctDays);
        Assert.Equal(0, family.FromWordCount); // both faulty forms are non-words
    }
}
