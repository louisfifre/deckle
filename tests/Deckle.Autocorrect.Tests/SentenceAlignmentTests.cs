using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Inverts SentenceCorpus.Flush: the typed side re-tokenizes with the canonical
// tokenizer, the final side either overlays the History transitions by slot index
// (a modern record) or is recovered by re-tokenizing the Final string (a legacy
// record, history property absent). Two integrity guards drop a record whose slot
// indexing cannot be trusted, rather than judge it against a fabricated final.
[Trait("Category", "unit")]
public sealed class SentenceAlignmentTests
{
    private static AlignmentResult AlignModern(string typed, string final, string history) =>
        SentenceAlignment.Align(new CorpusEntry(
            new SentenceCorpus.SentenceRecord(typed, final, history, "sentence", string.Empty),
            HistoryPresent: true));

    private static AlignmentResult AlignLegacy(string typed, string final) =>
        SentenceAlignment.Align(new CorpusEntry(
            new SentenceCorpus.SentenceRecord(typed, final, string.Empty, "sentence", string.Empty),
            HistoryPresent: false));

    [Fact]
    public void UnchangedSentenceAlignsTypedToItself()
    {
        AlignmentResult r = AlignModern("bonjour monde.", "bonjour monde.", "");

        Assert.Equal(AlignmentStatus.Aligned, r.Status);
        Assert.Equal(new[] { "bonjour", "monde" }, r.Typed);
        Assert.Equal(new[] { "bonjour", "monde" }, r.Final);
    }

    [Fact]
    public void OverlaysCommitStageFinalFormsBySlot()
    {
        // The record SentenceCorpusTests.RecordsTheCommitStageTransition emits.
        AlignmentResult r = AlignModern(
            "marche ecole.", "marché école.", "#0=marche»commit:marché|#1=ecole»commit:école");

        Assert.Equal(AlignmentStatus.Aligned, r.Status);
        Assert.Equal(new[] { "marche", "ecole" }, r.Typed);
        Assert.Equal(new[] { "marché", "école" }, r.Final);
    }

    [Fact]
    public void TakesTheLastTransitionAsTheFinalForm()
    {
        // « etant » retyped by hand then commit-repaired: user»commit, last wins.
        AlignmentResult r = AlignModern(
            "etant la.", "étant là.", "#0=etant»user:etant»commit:étant|#1=la»sentence:là");

        Assert.Equal(new[] { "etant", "la" }, r.Typed);
        Assert.Equal(new[] { "étant", "là" }, r.Final);
    }

    [Fact]
    public void TokenizesTheElisionApostropheAsTwoSlots()
    {
        AlignmentResult r = AlignModern("j'ai faim.", "j'ai faim.", "");

        Assert.Equal(new[] { "j'", "ai", "faim" }, r.Typed);
        Assert.Equal(new[] { "j'", "ai", "faim" }, r.Final);
    }

    // Fix (1): a legacy record whose final differs from typed must NOT collapse to
    // final=typed (which counted every live correction against the judge). When the
    // final tokenizes one-to-one with the typed side, it IS the final.
    [Fact]
    public void LegacyRecordRecoversTheFinalByReTokenizingIt()
    {
        AlignmentResult r = AlignLegacy("mise a jour.", "mise à jour.");

        Assert.Equal(AlignmentStatus.RepairedFromFinal, r.Status);
        Assert.Equal(new[] { "mise", "a", "jour" }, r.Typed);
        Assert.Equal(new[] { "mise", "à", "jour" }, r.Final);
    }

    // Fix (1) guard: an elision apostrophe re-split the final into more tokens than
    // the typed side, so no one-to-one slot map exists — skip, do not mis-align.
    [Fact]
    public void LegacyRecordWithTokenCountDriftIsSkipped()
    {
        AlignmentResult r = AlignLegacy("cetait bon.", "c'était bon.");

        Assert.False(r.Usable);
        Assert.Equal(AlignmentStatus.Unusable, r.Status);
    }

    // Fix (2): a corrupted typed string (a User re-edit into « d' » dropped the
    // separator and fused « de » + « avoir » into « deavoir ») shifts the history
    // indices; the first-typed « de » no longer matches the token « deavoir » at
    // index 0, so the record is unusable.
    [Fact]
    public void HistoryFirstTypedMismatchSkipsTheRecord()
    {
        AlignmentResult r = AlignModern("deavoir besoin.", "d'avoir besoin.", "#0=de»user:d'");

        Assert.False(r.Usable);
        Assert.Equal(AlignmentStatus.Unusable, r.Status);
    }

    // An out-of-range slot index is the same broken-indexing signal — skip, not a
    // silent ignore (the pre-integrity behaviour that hid the drift).
    [Fact]
    public void OutOfRangeSlotReferenceSkipsTheRecord()
    {
        AlignmentResult r = AlignModern("un deux.", "un deux.", "#9=x»commit:y");

        Assert.False(r.Usable);
    }

    [Fact]
    public void ParsesEachChangedSlotOnce()
    {
        var parsed = SentenceAlignment.ParseHistory(
            "#0=marche»commit:marché|#1=ecole»commit:école").ToArray();

        Assert.Equal(new[] { (0, "marché"), (1, "école") }, parsed);
    }

    [Fact]
    public void ParsesTheFirstTypedFormForTheIntegrityCheck()
    {
        var parsed = SentenceAlignment.ParseHistoryEntries(
            "#0=etant»user:etant»commit:étant|#1=la»sentence:là").ToArray();

        Assert.Equal(new[] { (0, "etant", "étant"), (1, "la", "là") }, parsed);
    }
}
