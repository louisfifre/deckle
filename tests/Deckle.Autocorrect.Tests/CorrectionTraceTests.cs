using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The decision ledger threaded through the synchronous chain — the observable
// surface of the per-word telemetry. These assert what a stage RECORDS (outcome,
// decisive reason, candidate pool, the per-stage trail), the diagnostic Louis
// reads, not how the chain computes it. Reasons are asserted against the closed
// vocabulary constants the code itself emits, so a re-tune of a threshold never
// breaks them.
[Trait("Category", "unit")]
public class CorrectionTraceTests
{
    // étant/êtant: the synthetic dominance pair (100 vs 1 clears the 20× ratio).
    private const string FrenchTsv =
        "français\t400\nétant\t100\nêtant\t1\nmarche\t85\nmarché\t90\n";

    private static FrequencyLexicon French() => FrequencyLexicon.LoadTsv(new StringReader(FrenchTsv));

    private static DiacriticsRestorer Diacritics(RestorerOptions? options = null)
    {
        var french = French();
        return new DiacriticsRestorer(french, english: null, AccentIndex.Build(french), options);
    }

    private static ConservativeTypoCorrector Typo() =>
        new(French(), english: null, personal: null, options: null);

    // ── A correction fired ──────────────────────────────────────────────────

    [Fact]
    public void RecordsAFiredLexicalGate()
    {
        var trace = new CorrectionTrace();
        Diacritics().Evaluate("francais", [], trace);

        Assert.Equal(CorrectionTrace.Outcomes.Corrected, trace.Outcome);
        Assert.Equal(CorrectionTrace.StageNames.Diacritics, trace.PrimaryStage);
        Assert.Equal(CorrectionTrace.Reasons.LexicalGate, trace.PrimaryReason);
        Assert.Contains("français", trace.RenderCandidates());
    }

    // ── A literal left alone, with the deciding guard visible ─────────────────

    [Fact]
    public void RecordsTheGuardThatLeftAValidWordAlone()
    {
        var trace = new CorrectionTrace();
        var decision = Diacritics().Evaluate("marche", [], trace);

        Assert.Null(decision);
        Assert.Equal(CorrectionTrace.Outcomes.Literal, trace.Outcome);
        Assert.Equal(CorrectionTrace.Reasons.ValidFrench, trace.PrimaryReason);
    }

    // ── The safety gauges of the dominance path are surfaced with their bounds ─

    [Fact]
    public void RecordsDominanceGaugesAgainstTheirThresholds()
    {
        var trace = new CorrectionTrace();
        Diacritics().Evaluate("etant", [], trace);

        Assert.Equal(CorrectionTrace.Reasons.FrequencyDominance, trace.PrimaryReason);
        string gauges = trace.RenderGauges();
        // The magnitude and its threshold both appear, so the margin reads off the
        // line — names, not values, are the contract.
        Assert.Contains("dominance", gauges);
        Assert.Contains("dominance_min", gauges);
        Assert.Contains("top_freq", gauges);
    }

    // ── The typo stage records its tier and the neighbour it chose ────────────

    [Fact]
    public void RecordsATypoNearCorrection()
    {
        var trace = new CorrectionTrace();
        var decision = Typo().Evaluate("marhce", [], trace);

        Assert.NotNull(decision);
        Assert.Equal("marche", decision!.Replacement);
        Assert.Equal(CorrectionTrace.StageNames.Typo, trace.PrimaryStage);
        Assert.Equal(CorrectionTrace.Reasons.TypoNear, trace.PrimaryReason);
        Assert.Contains("marche", trace.RenderCandidates());
    }

    // ── The trail spans every stage the composite ran, in order ───────────────

    [Fact]
    public void TrailSpansEveryStageForALiteral()
    {
        var french = French();
        var policy = new CompositeCorrectionPolicy(
            new DiacriticsRestorer(french, english: null, AccentIndex.Build(french)),
            new ElisionCorrector(french),
            new ConservativeTypoCorrector(french));

        var trace = new CorrectionTrace();
        policy.Evaluate("marche", [], trace); // a valid word: every stage stands aside

        string trail = trace.RenderTrail();
        Assert.Contains(CorrectionTrace.StageNames.Diacritics, trail);
        Assert.Contains(CorrectionTrace.StageNames.Elision, trail);
        Assert.Contains(CorrectionTrace.StageNames.Typo, trail);
        Assert.Equal(CorrectionTrace.Outcomes.Literal, trace.Outcome);
    }

    // ── A fired stage stops the chain: later stages never run ─────────────────

    [Fact]
    public void TrailStopsAtTheFiringStage()
    {
        var french = French();
        var policy = new CompositeCorrectionPolicy(
            new DiacriticsRestorer(french, english: null, AccentIndex.Build(french)),
            new ElisionCorrector(french),
            new ConservativeTypoCorrector(french));

        var trace = new CorrectionTrace();
        policy.Evaluate("francais", [], trace); // diacritics fires; elision/typo never run

        string trail = trace.RenderTrail();
        Assert.Contains(CorrectionTrace.StageNames.Diacritics, trail);
        Assert.DoesNotContain(CorrectionTrace.StageNames.Typo, trail);
        Assert.Equal(CorrectionTrace.Outcomes.Corrected, trace.Outcome);
    }

    // ── A learned-revert veto reads as suppressed, not corrected ──────────────

    [Fact]
    public void MarkSuppressedOverridesAFiredOutcome()
    {
        var trace = new CorrectionTrace();
        Diacritics().Evaluate("francais", [], trace); // a stage fires…
        trace.MarkSuppressed();                        // …then a learned revert vetoes it

        Assert.Equal(CorrectionTrace.Outcomes.Suppressed, trace.Outcome);
    }
}
