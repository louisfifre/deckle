using Deckle.Lighting.Ambient;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientPushCadenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransportPreferenceOverridesPipelineShape(bool multiLight)
        => Assert.Equal(50, AmbientPushCadence.ResolveRateHz(50, multiLight));

    [Theory]
    [InlineData(false, 15)]
    [InlineData(true, 10)]
    public void MissingTransportPreferenceKeepsRestCadence(bool multiLight, int expectedRateHz)
        => Assert.Equal(expectedRateHz, AmbientPushCadence.ResolveRateHz(null, multiLight));

    [Theory]
    [InlineData(0.15)]
    [InlineData(0.40)]
    [InlineData(0.80)]
    public void AdaptedSmoothingKeepsTheSameResponseOverOneSecond(double referenceAlpha)
    {
        double streamingAlpha = AmbientPushCadence.AdaptSmoothingAlpha(referenceAlpha, 50);
        double referenceRemaining = Math.Pow(1.0 - referenceAlpha, 15);
        double streamingRemaining = Math.Pow(1.0 - streamingAlpha, 50);

        Assert.Equal(referenceRemaining, streamingRemaining, precision: 12);
    }

    [Fact]
    public void OnTimeTickKeepsTheNextScheduledDeadline()
    {
        long next = AmbientPushCadence.AdvanceDeadline(
            previousDeadline: 1_000,
            tickStartedAt: 1_000,
            tickCompletedAt: 1_010,
            intervalTicks: 20,
            out long skippedSlots);

        Assert.Equal(1_020, next);
        Assert.Equal(0, skippedSlots);
    }

    [Fact]
    public void LongTickSkipsExpiredSlotsInsteadOfReplayingThem()
    {
        long next = AmbientPushCadence.AdvanceDeadline(
            previousDeadline: 1_000,
            tickStartedAt: 1_000,
            tickCompletedAt: 1_051,
            intervalTicks: 20,
            out long skippedSlots);

        Assert.Equal(1_060, next);
        Assert.Equal(2, skippedSlots);
    }

    [Fact]
    public void LateWakeCoalescesTheNearCatchUpSlot()
    {
        long next = AmbientPushCadence.AdvanceDeadline(
            previousDeadline: 1_000,
            tickStartedAt: 1_019,
            tickCompletedAt: 1_019,
            intervalTicks: 20,
            out long skippedSlots);

        Assert.Equal(1_039, next);
        Assert.Equal(0, skippedSlots);
    }

    [Fact]
    public void WholeSlotsMissedBeforeTheTickAreCounted()
    {
        long next = AmbientPushCadence.AdvanceDeadline(
            previousDeadline: 1_000,
            tickStartedAt: 1_041,
            tickCompletedAt: 1_041,
            intervalTicks: 20,
            out long skippedSlots);

        Assert.Equal(1_061, next);
        Assert.Equal(2, skippedSlots);
    }
}
