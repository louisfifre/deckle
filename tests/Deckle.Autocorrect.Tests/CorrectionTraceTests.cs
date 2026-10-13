using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The decision ledger threaded through the synchronous chain — the observable
// surface of the per-word telemetry. These assert what a stage RECORDS (outcome,
// decisive reason, candidate pool, the per-stage trail), not how the chain
// computes it. Reasons are asserted against the closed vocabulary constants the
// code itself emits, so a re-tune of a threshold never breaks them.
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

    [Fact]
    public void RecordsTheGuardThatLeftAValidWordAlone()
    {
        var trace = new CorrectionTrace();
        var decision = Diacritics().Evaluate("marche", [], trace);

        Assert.Null(decision);
        Assert.Equal(CorrectionTrace.Outcomes.Literal, trace.Outcome);
        Assert.Equal(CorrectionTrace.Reasons.ValidFrench, trace.PrimaryReason);
    }

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

    [Fact]
    public void TrailSpansEveryStageForALiteral()
    {
        var french = French();
        var policy = new CompositeCorrectionPolicy(
            new DiacriticsRestorer(french, english: null, AccentIndex.Build(french)),
            new ElisionCorrector(french),
            new ConservativeTypoCorrector(french));

        var trace = new CorrectionTrace();
        policy.Evaluate("marche", [], trace);

        string trail = trace.RenderTrail();
        Assert.Contains(CorrectionTrace.StageNames.Diacritics, trail);
        Assert.Contains(CorrectionTrace.StageNames.Elision, trail);
        Assert.Contains(CorrectionTrace.StageNames.Typo, trail);
        Assert.Equal(CorrectionTrace.Outcomes.Literal, trace.Outcome);
    }

    [Fact]
    public void TrailStopsAtTheFiringStage()
    {
        var french = French();
        var policy = new CompositeCorrectionPolicy(
            new DiacriticsRestorer(french, english: null, AccentIndex.Build(french)),
            new ElisionCorrector(french),
            new ConservativeTypoCorrector(french));

        var trace = new CorrectionTrace();
        policy.Evaluate("francais", [], trace);

        string trail = trace.RenderTrail();
        Assert.Contains(CorrectionTrace.StageNames.Diacritics, trail);
        Assert.DoesNotContain(CorrectionTrace.StageNames.Typo, trail);
        Assert.Equal(CorrectionTrace.Outcomes.Corrected, trace.Outcome);
    }

    [Fact]
    public void MarkSuppressedOverridesAFiredOutcome()
    {
        var trace = new CorrectionTrace();
        Diacritics().Evaluate("francais", [], trace);
        trace.MarkSuppressed();

        Assert.Equal(CorrectionTrace.Outcomes.Suppressed, trace.Outcome);
    }
}
