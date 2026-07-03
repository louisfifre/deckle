using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The margin sweep over replay results collected at margin 0: raising the bar
// drops the low-margin slots, trading coverage for precision. Pure, scripted
// results — no corpus, no model.
[Trait("Category", "unit")]
public sealed class MarginCalibrationTests
{
    // margin 0 → judge returns its argmax and the raw gap; chosen==final agrees.
    private static SlotReplayResult Judged(string final, string chosen, double margin) =>
        new(TypedForm: "a", FinalForm: final, SlotIndex: 0, JudgeChosen: chosen,
            Margin: margin, Threshold: 0.0, AbstainReason: null);

    private static SlotReplayResult Errored() =>
        new("a", "à", 0, JudgeChosen: null, 0.0, 0.0, RerankOutcome.AbstainReasons.Error);

    private static readonly SlotReplayResult[] Results =
    {
        Judged(final: "à", chosen: "à", margin: 2.0),  // agree, wide
        Judged(final: "à", chosen: "à", margin: 0.5),  // agree, narrow
        Judged(final: "la", chosen: "là", margin: 1.0), // disagree
        Errored(),                                       // model error: never applied
    };

    [Fact]
    public void RaisingTheBarTradesCoverageForPrecision()
    {
        var rows = MarginCalibration.Sweep(Results, new[] { 0.0, 1.0, 2.0 });

        // t=0: applies the 3 non-errored slots, 2 agree → 2/3 precision, 3/4 reach.
        Assert.Equal(new CalibrationRow(0.0, Applied: 3, Agreed: 2, Held: 1, Precision: 2.0 / 3, Coverage: 3.0 / 4), rows[0]);
        // t=1: the 0.5-margin slot drops; the wide agree and the disagree remain.
        Assert.Equal(new CalibrationRow(1.0, Applied: 2, Agreed: 1, Held: 2, Precision: 0.5, Coverage: 2.0 / 4), rows[1]);
        // t=2: only the widest slot survives, and it agrees → perfect but thin.
        Assert.Equal(new CalibrationRow(2.0, Applied: 1, Agreed: 1, Held: 3, Precision: 1.0, Coverage: 1.0 / 4), rows[2]);
    }

    [Fact]
    public void RenderCarriesTheCountsAndTheCurve()
    {
        var summary = new ReplaySummary(Sentences: 2, AmbiguousSlots: 4, Chosen: 3, Abstained: 1, AgreedWithFinal: 2);
        var rows = MarginCalibration.Sweep(Results, new[] { 0.0, 2.0 });

        string report = MarginCalibration.Render(summary, rows);

        Assert.Contains("2 sentences, 4 ambiguous slots", report);
        Assert.Contains("1 model abstentions", report);
        Assert.Contains("| margin ≥ | applied | agree | held | precision | coverage |", report);
        Assert.Contains("| 2.00 | 1 | 1 | 3 | 100.0% | 25.0% |", report);
    }
}
