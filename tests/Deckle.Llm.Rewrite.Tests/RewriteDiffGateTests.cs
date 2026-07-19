using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// Unit tests of the diff gate's contract: three rules under strictly
// monotone order, all-or-nothing. The framing examples of 2026-07-19 are
// pinned as-is ("samarreter" passes, "voiture" → "véhicule" blocks) — they
// are the contract, not an illustration of it.
public class RewriteDiffGateTests
{
    // ── Identity and pure form repairs ──────────────────────────────────

    [Fact]
    public void IdenticalTextIsAcceptedIdentity()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "Je pense que ça marche.",
            "Je pense que ça marche.");

        Assert.True(verdict.Accepted);
        Assert.True(verdict.IsIdentity);
    }

    [Fact]
    public void SentenceBoundariesAndAccentsAreAccepted()
    {
        // The retaille's core job: create the missing sentence frontier,
        // restore accents and capitalization — form only, every word kept.
        var verdict = RewriteDiffGate.Evaluate(
            "je pense que ca marche pas mal la suite arrive demain",
            "Je pense que ça marche pas mal. La suite arrive demain.");

        Assert.True(verdict.Accepted);
        Assert.False(verdict.IsIdentity);
    }

    [Fact]
    public void ElisionRepairsAreAccepted()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "jai fini cest bon",
            "j'ai fini, c'est bon.");

        Assert.True(verdict.Accepted);
    }

    [Fact]
    public void PhoneticResegmentationIsAccepted()
    {
        // The framing example: "samarreter" ≈ "sans m'arrêter" — one typed
        // glob split into the words it was, within the form bound.
        var verdict = RewriteDiffGate.Evaluate(
            "je bosse samarreter depuis ce matin",
            "je bosse sans m'arrêter depuis ce matin");

        Assert.True(verdict.Accepted);
    }

    // ── Deletions: duplicates and crutches only ─────────────────────────

    [Fact]
    public void AdjacentDuplicateDeletionIsAccepted()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "le le chat dort",
            "le chat dort");

        Assert.True(verdict.Accepted);
    }

    [Fact]
    public void FillerDeletionIsAccepted()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "euh je pense que ça marche",
            "je pense que ça marche");

        Assert.True(verdict.Accepted);
    }

    [Fact]
    public void FillerPhraseDeletionIsAccepted()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "du coup je pense que ça marche",
            "je pense que ça marche");

        Assert.True(verdict.Accepted);
    }

    [Fact]
    public void ContentWordDeletionIsRejected()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "je pense que ça marche vraiment bien",
            "je pense que ça marche bien");

        Assert.False(verdict.Accepted);
        Assert.Contains(verdict.Edits, e => e.Ruling == DiffEditRuling.RejectedDeletion);
    }

    [Fact]
    public void EmptyRewriteIsRejected()
    {
        var verdict = RewriteDiffGate.Evaluate("je pense que ça marche", "");

        Assert.False(verdict.Accepted);
    }

    // ── Insertions: closed classes only ─────────────────────────────────

    [Fact]
    public void FunctionWordInsertionIsAccepted()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "ça marche pas",
            "ça ne marche pas");

        Assert.True(verdict.Accepted);
    }

    [Fact]
    public void ContentWordInsertionIsRejected()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "le chat dort",
            "le chat dort profondément");

        Assert.False(verdict.Accepted);
        Assert.Contains(verdict.Edits, e => e.Ruling == DiffEditRuling.RejectedInsertion);
    }

    // ── Replacements: bounded form distance ─────────────────────────────

    [Fact]
    public void VocabularySubstitutionIsRejected()
    {
        // The framing counter-example: same meaning, different word — the
        // exact edit the gate exists to block.
        var verdict = RewriteDiffGate.Evaluate(
            "ma voiture est rouge",
            "ma véhicule est rouge");

        Assert.False(verdict.Accepted);
        Assert.Contains(verdict.Edits, e => e.Ruling == DiffEditRuling.RejectedReplacement);
    }

    [Fact]
    public void FrancizationOfATechnicalTermIsRejected()
    {
        // Measured false accept on the 2026-07-19 eval at the 34 % bound:
        // "gate" → "gâteau" is a vocabulary corruption, not a form repair.
        var verdict = RewriteDiffGate.Evaluate(
            "le gate rejette tout",
            "le gâteau rejette tout");

        Assert.False(verdict.Accepted);
    }

    [Fact]
    public void GroupDilutionCannotHideASubstitution()
    {
        // Measured false accept on the 2026-07-19 eval: grouped as one
        // 2→2 replacement, the identical "lisible" diluted the concatenated
        // form distance and let "pas" → "peu" through. A word that survives
        // verbatim must be matched, never grouped.
        var verdict = RewriteDiffGate.Evaluate(
            "cest pas lisible",
            "c'est peu lisible");

        Assert.False(verdict.Accepted);
    }

    [Fact]
    public void MarkdownFormattingInsertionIsRejected()
    {
        // Measured false accept on the 2026-07-19 eval: models decorate
        // offers with markdown bold; formatting characters are not
        // punctuation to this gate.
        var verdict = RewriteDiffGate.Evaluate(
            "le total fait 54",
            "le total fait **54**");

        Assert.False(verdict.Accepted);
    }

    [Fact]
    public void NumbersNeverDrift()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "il y a 2026 lignes",
            "il y a 2027 lignes");

        Assert.False(verdict.Accepted);
    }

    // ── Order and all-or-nothing ────────────────────────────────────────

    [Fact]
    public void ReorderedWordsAreRejected()
    {
        // Monotone strict: the same words in another order are not "your
        // words" any more.
        var verdict = RewriteDiffGate.Evaluate(
            "le chat noir dort",
            "le noir chat dort");

        Assert.False(verdict.Accepted);
    }

    [Fact]
    public void OneViolationRejectsTheWholeParagraph()
    {
        // Every other edit is impeccable — filler dropped, capitalization,
        // final period. The single substitution still kills the offer:
        // all-or-nothing, no per-edit filtering in V1.
        var verdict = RewriteDiffGate.Evaluate(
            "euh je pense que ma voiture est rouge",
            "Je pense que ma véhicule est rouge.");

        Assert.False(verdict.Accepted);
        Assert.Contains(verdict.Edits, e => e.Ruling == DiffEditRuling.AllowedDeletion);
        Assert.Contains(verdict.Edits, e => e.Ruling == DiffEditRuling.RejectedReplacement);
    }

    [Fact]
    public void VerdictCarriesTheFullScriptInReadingOrder()
    {
        var verdict = RewriteDiffGate.Evaluate(
            "jai dit que ca marche",
            "J'ai dit que ça marche.");

        // Script covers both sides entirely: concatenating the original
        // column of every edit gives back the original words.
        string rebuiltOriginal = string.Join(" ",
            verdict.Edits
                .Where(e => e.Original.Length > 0)
                .Select(e => e.Original));
        Assert.Equal("jai dit que ca marche", rebuiltOriginal);
    }
}
