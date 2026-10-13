using Deckle.Composition;
using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// Near-black behaviour of the colour path : the black gate, and what the
// 8-bit output does on dark sequences once tuning, smoothing and the
// single quantisation run together.
[Trait("Category", "unit")]
public sealed class AmbientNearBlackTests
{
    // The capture runs at 60 Hz and the push loop at Hue Entertainment's
    // 50 Hz ; each push reads the latest captured frame.
    private const int CaptureRateHz = 60;
    private const int PushRateHz = 50;
    private const int SimulatedSeconds = 4;
    private const int SettlingTicks = PushRateHz;

    [Fact]
    [Trait("Category", "regression")]
    public void DarkButNonBlackFrameStaysLitWithFloorOff()
    {
        LightColor dark = TuneAndQuantize(LinearOfEncoded(8), IdentityCurve());
        LightColor black = TuneAndQuantize((0f, 0f, 0f), IdentityCurve());

        Assert.NotEqual(LightColor.Black, dark);
        Assert.Equal(LightColor.Black, black);
    }

    [Fact]
    public void ExactlyBlackFrameWithFloorOnGivesNeutralFloor()
    {
        var settings = new AmbientSettings { MinBrightnessEnabled = true };
        var linear = (0f, 0f, 0f);

        var tuned = AmbientColorPipeline.ApplyTuning(
            SrgbColor.FromLinear(linear),
            AmbientColorPipeline.IsBlackSource(linear),
            settings.SaturationBoost,
            settings.BrightnessCurveX1,
            settings.BrightnessCurveY1,
            settings.BrightnessCurveX2,
            settings.BrightnessCurveY2,
            settings.MinBrightnessEnabled,
            settings.MinBrightness);

        byte floor = (byte)settings.MinBrightness;
        Assert.Equal(new LightColor(floor, floor, floor), tuned.Quantize());
    }

    public static TheoryData<bool, bool> CurveAndAlphaSources => new()
    {
        { false, false },
        { false, true },
        { true, false },
        { true, true },
    };

    [Theory]
    [MemberData(nameof(CurveAndAlphaSources))]
    public void SteadyBlackSequenceReachesBlackAndStays(bool ambientCurve, bool ambientAlpha)
    {
        var output = Simulate(_ => 0, ambientCurve, ambientAlpha);

        Assert.All(output.Skip(SettlingTicks), c => Assert.Equal(LightColor.Black, c));
    }

    [Theory]
    [MemberData(nameof(CurveAndAlphaSources))]
    public void SteadyDarkSequenceNeverBecomesBlack(bool ambientCurve, bool ambientAlpha)
    {
        var output = Simulate(_ => 8, ambientCurve, ambientAlpha);

        Assert.All(output, c => Assert.NotEqual(LightColor.Black, c));
    }

    [Theory]
    [MemberData(nameof(CurveAndAlphaSources))]
    public void BlackAlternatingWithOneDoesNotToggleAfterSettling(bool ambientCurve, bool ambientAlpha)
    {
        var output = Simulate(frame => frame % 2 == 0 ? (byte)0 : (byte)1, ambientCurve, ambientAlpha);

        Assert.InRange(CountBlackToggles(output.Skip(SettlingTicks).ToList()), 0, 1);
    }

    [Theory]
    [MemberData(nameof(CurveAndAlphaSources))]
    public void BlackAlternatingWithTwoDoesNotToggleAfterSettling(bool ambientCurve, bool ambientAlpha)
    {
        var output = Simulate(frame => frame % 2 == 0 ? (byte)0 : (byte)2, ambientCurve, ambientAlpha);

        Assert.InRange(CountBlackToggles(output.Skip(SettlingTicks).ToList()), 0, 1);
    }

    // Under the gamma-like curve (0.42, 0, 1, 1), 6/255 maps to ~0.26/255 and
    // 10/255 to ~0.71/255 : one on each side of the half step (0.5/255) at
    // which plain rounding turns a value into black. A grey sequence that
    // switches between them every half second makes the smoothed value
    // cross that boundary again and again, while the whole swing stays
    // inside the latch's band (enter black below 0.25/255, leave above
    // 0.75/255), so the latch has no reason to toggle.
    private const byte BelowHalfStepLevel = 6;
    private const byte AboveHalfStepLevel = 10;
    private const int SwitchEveryFrames = CaptureRateHz / 2;

    public static TheoryData<bool> SmoothingAlphas => new() { false, true };

    [Theory]
    [MemberData(nameof(SmoothingAlphas))]
    [Trait("Category", "regression")]
    public void LevelsAcrossTheRoundingBoundaryDoNotToggleBlackOnceLatched(bool ambientAlpha)
    {
        byte LevelAtFrame(int frame)
            => frame / SwitchEveryFrames % 2 == 0 ? BelowHalfStepLevel : AboveHalfStepLevel;

        var latched = Simulate(LevelAtFrame, ambientCurve: false, ambientAlpha, new AmbientBlackLatch().Quantize);
        var rounded = Simulate(LevelAtFrame, ambientCurve: false, ambientAlpha, (_, smoothed) => smoothed.Quantize());

        // The sequence is faulty for plain rounding : without the latch the
        // lamp goes on and off with the smoothed value.
        Assert.True(
            CountBlackToggles(rounded.Skip(SettlingTicks).ToList()) >= 2,
            "plain rounding was expected to toggle black at the boundary");
        Assert.InRange(CountBlackToggles(latched.Skip(SettlingTicks).ToList()), 0, 1);
    }

    private static List<LightColor> Simulate(Func<int, byte> encodedLevelAtFrame, bool ambientCurve, bool ambientAlpha)
        => Simulate(encodedLevelAtFrame, ambientCurve, ambientAlpha, new AmbientBlackLatch().Quantize);

    // Feeds a 60 Hz sequence of grey frames (encoded byte level per frame)
    // through the push-side path at 50 Hz, floor off : black gate,
    // tuning, smoothing over the tick interval, then the given quantiser
    // (called with the key "group" and the smoothed colour).
    private static List<LightColor> Simulate(
        Func<int, byte> encodedLevelAtFrame,
        bool ambientCurve,
        bool ambientAlpha,
        Func<string, SrgbColor, LightColor> quantize)
    {
        AmbientSettings curve = SettingsFor(ambientCurve);
        double alpha = SettingsFor(ambientAlpha).SmoothingAlpha;
        var smoother = new AmbientColorSmoother();
        var output = new List<LightColor>();

        for (int tick = 0; tick < SimulatedSeconds * PushRateHz; tick++)
        {
            int frame = tick * CaptureRateHz / PushRateHz;
            var linear = LinearOfEncoded(encodedLevelAtFrame(frame));
            SrgbColor tuned = Tune(linear, curve);
            SrgbColor smoothed = smoother.Smooth("group", tuned, alpha, 1.0 / PushRateHz);
            output.Add(quantize("group", smoothed));
        }

        return output;
    }

    private static int CountBlackToggles(IReadOnlyList<LightColor> output)
    {
        int toggles = 0;
        for (int i = 1; i < output.Count; i++)
        {
            if ((output[i] == LightColor.Black) != (output[i - 1] == LightColor.Black))
                toggles++;
        }
        return toggles;
    }

    private static LightColor TuneAndQuantize((float R, float G, float B) linear, AmbientSettings curve)
        => new AmbientBlackLatch().Quantize("group", Tune(linear, curve));

    private static SrgbColor Tune((float R, float G, float B) linear, AmbientSettings curve)
        => AmbientColorPipeline.ApplyTuning(
            SrgbColor.FromLinear(linear),
            AmbientColorPipeline.IsBlackSource(linear),
            saturationBoost: 1.0,
            curve.BrightnessCurveX1,
            curve.BrightnessCurveY1,
            curve.BrightnessCurveX2,
            curve.BrightnessCurveY2,
            minBrightnessEnabled: false,
            minBrightness: 0);

    // The two tunings the sequences run under. True : the Ambient preset
    // (the shadow-lifting curve, the slow alpha). False : the gamma-like
    // curve (0.42, 0, 1, 1) with alpha 0.30, the reference tuning the
    // rounding-boundary sequence is built for.
    private static AmbientSettings SettingsFor(bool ambientPreset)
    {
        var settings = new AmbientSettings();
        if (ambientPreset)
        {
            AmbientPresets.All.Single(p => p.Id == AmbientPresets.Ambient).Values.ApplyTo(settings);
        }
        else
        {
            settings.BrightnessCurveX1 = 0.42;
            settings.BrightnessCurveY1 = 0.00;
            settings.BrightnessCurveX2 = 1.00;
            settings.BrightnessCurveY2 = 1.00;
            settings.SmoothingAlpha = 0.30;
        }
        return settings;
    }

    // The identity curve : control points on the diagonal.
    private static AmbientSettings IdentityCurve() => new()
    {
        BrightnessCurveX1 = 1.0 / 3.0,
        BrightnessCurveY1 = 1.0 / 3.0,
        BrightnessCurveX2 = 2.0 / 3.0,
        BrightnessCurveY2 = 2.0 / 3.0,
    };

    private static (float R, float G, float B) LinearOfEncoded(byte level)
    {
        float linear = ColorSpace.SrgbToLinear8Lut[level];
        return (linear, linear, linear);
    }
}
