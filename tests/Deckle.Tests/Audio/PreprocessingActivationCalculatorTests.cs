using System.Collections.Generic;
using Deckle.Audio.Preprocessing;
using Deckle.Audio.Telemetry;
using Xunit;

namespace Deckle.Tests.Audio;

// The deferred-activation decision is pure signal arithmetic over the
// ring of recent recordings: it reads each take's MeanDbfs and decides
// Active / Dormant on the median deficit versus the makeup target. These
// tests pin that arithmetic. The activation THRESHOLD value itself is
// provisional (to be ground-truthed by the WER bench) and is passed in
// explicitly so the tests don't depend on the default.
[Trait("Category", "unit")]
public class PreprocessingActivationCalculatorTests
{
    // Only MeanDbfs is read by the calculator; the rest is filler.
    private static MicrophoneTelemetryPayload WithMean(double meanDbfs) =>
        new(
            DurationSeconds: 5, Samples: 100,
            MinDbfs: -90, P10Dbfs: -60, P25Dbfs: -50, P50Dbfs: -40,
            P75Dbfs: -30, P90Dbfs: -25, MaxDbfs: -10,
            MeanRms: 0, MeanDbfs: meanDbfs,
            TailRms: 0, TailDbfs: -60, TailState: "quiet");

    private static List<MicrophoneTelemetryPayload> Means(params double[] vals)
    {
        var list = new List<MicrophoneTelemetryPayload>();
        foreach (var v in vals) list.Add(WithMean(v));
        return list;
    }

    [Fact]
    public void StaysCalibratingUntilEnoughSamples()
    {
        var r = PreprocessingActivationCalculator.Evaluate(
            Means(-30, -30), needed: 5, targetRmsDbfs: -20, activationDeltaDb: 6);

        Assert.False(r.Decided);
        Assert.Equal(PreprocessingActivation.Calibrating, r.State);
    }

    [Fact]
    public void ActivatesWhenMicMedianSitsBelowTargetByDelta()
    {
        // Median mean ≈ -30, target -20 → deficit 10 dB ≥ threshold 6 → Active.
        var r = PreprocessingActivationCalculator.Evaluate(
            Means(-31, -30, -29, -30, -30), needed: 5, targetRmsDbfs: -20, activationDeltaDb: 6);

        Assert.True(r.Decided);
        Assert.Equal(PreprocessingActivation.Active, r.State);
        Assert.Equal(10.0, r.MedianDeltaDb, 0.5);
    }

    [Fact]
    public void StaysDormantWhenMicAlreadyAdequate()
    {
        // Median mean ≈ -18, target -20 → deficit -2 dB < threshold → Dormant.
        var r = PreprocessingActivationCalculator.Evaluate(
            Means(-18, -19, -18, -17, -18), needed: 5, targetRmsDbfs: -20, activationDeltaDb: 6);

        Assert.True(r.Decided);
        Assert.Equal(PreprocessingActivation.Dormant, r.State);
    }

    [Fact]
    public void UsesMedianSoOneRogueTakeDoesNotDecide()
    {
        // Four quiet takes + one loud outlier: the median stays quiet, so
        // the decision is Active despite the outlier.
        var r = PreprocessingActivationCalculator.Evaluate(
            Means(-30, -31, -29, -30, -5), needed: 5, targetRmsDbfs: -20, activationDeltaDb: 6);

        Assert.True(r.Decided);
        Assert.Equal(PreprocessingActivation.Active, r.State);
    }
}
