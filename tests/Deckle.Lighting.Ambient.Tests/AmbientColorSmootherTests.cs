using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientColorSmootherTests
{
    private const string Key = "light";
    private const double Alpha = 0.30;
    private const double Tick = 1.0 / 50;

    private static readonly SrgbColor Red = new(1f, 0f, 0f);
    private static readonly SrgbColor Blue = new(0f, 0f, 1f);

    [Fact]
    public void TheFirstTargetIsAdoptedWithoutFadeIn()
    {
        var smoother = new AmbientColorSmoother();

        Assert.Equal(Red, smoother.Smooth(Key, Red, Alpha, Tick));
    }

    [Fact]
    public void ALateTickCatchesUpLikeTheTicksItReplaced()
    {
        var steady = new AmbientColorSmoother();
        steady.Smooth(Key, Red, Alpha, Tick);
        steady.Smooth(Key, Blue, Alpha, Tick);
        steady.Smooth(Key, Blue, Alpha, Tick);
        SrgbColor afterRegularTicks = steady.Smooth(Key, Blue, Alpha, Tick);

        var late = new AmbientColorSmoother();
        late.Smooth(Key, Red, Alpha, Tick);
        late.Smooth(Key, Blue, Alpha, Tick);
        SrgbColor afterLateTick = late.Smooth(Key, Blue, Alpha, 2 * Tick);

        Assert.Equal(afterRegularTicks.R, afterLateTick.R, precision: 5);
        Assert.Equal(afterRegularTicks.B, afterLateTick.B, precision: 5);
    }

    [Fact]
    public void AResetKeyAdoptsItsNextTarget()
    {
        var smoother = new AmbientColorSmoother();
        smoother.Smooth(Key, Red, Alpha, Tick);

        smoother.Reset(Key);

        Assert.Equal(Blue, smoother.Smooth(Key, Blue, Alpha, Tick));
    }

    [Fact]
    public void ANaNAlphaDoesNotPoisonTheTrail()
    {
        var smoother = new AmbientColorSmoother();
        smoother.Smooth(Key, Red, Alpha, Tick);

        SrgbColor result = smoother.Smooth(Key, Blue, double.NaN, Tick);
        SrgbColor next = smoother.Smooth(Key, Blue, Alpha, Tick);

        Assert.Equal(Blue, result);
        Assert.Equal(Blue, next);
    }

    [Fact]
    public void ANaNOrOutOfRangeTargetKeepsTheStateFiniteAndInRange()
    {
        var smoother = new AmbientColorSmoother();
        smoother.Smooth(Key, Red, Alpha, Tick);

        smoother.Smooth(Key, new SrgbColor(float.NaN, 2f, -1f), Alpha, Tick);
        SrgbColor next = smoother.Smooth(Key, Red, Alpha, Tick);

        foreach (float channel in new[] { next.R, next.G, next.B })
        {
            Assert.False(float.IsNaN(channel));
            Assert.InRange(channel, 0f, 1f);
        }
    }
}
