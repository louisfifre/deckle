using Deckle.Input;
using Deckle.Input.PrecisionScroll;
using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

[Trait("Category", "unit")]
public sealed class PrecisionScrollGestureTests
{
    [Fact]
    public void OneDetentProducesOneContinuousGesture()
    {
        var gesture = new PrecisionScrollGesture();
        gesture.AddDetent(direction: 1, sensitivity: 1, timestampMs: 0);

        Assert.True(gesture.TryAdvance(0, out var begin));
        Assert.Equal(PrecisionScrollFrameKind.Begin, begin.Kind);
        Assert.Equal(1_000, begin.First.Y);
        Assert.False(gesture.TryAdvance(9, out _));

        var frames = AdvanceUntilEnded(gesture, startMs: 10);

        Assert.Contains(frames, frame => frame.Kind == PrecisionScrollFrameKind.Move);
        Assert.Equal(PrecisionScrollFrameKind.End, frames[^1].Kind);
        Assert.False(gesture.IsActive);
    }

    [Fact]
    public void BacklogIncreasesContactVelocityWithinTheBound()
    {
        var oneTick = new PrecisionScrollGesture();
        oneTick.AddDetent(1, 1, 0);
        oneTick.TryAdvance(0, out var oneBegin);
        oneTick.TryAdvance(10, out var oneMove);

        var twoTicks = new PrecisionScrollGesture();
        twoTicks.AddDetent(1, 1, 0);
        twoTicks.AddDetent(1, 1, 5);
        twoTicks.TryAdvance(5, out var twoBegin);
        twoTicks.TryAdvance(15, out var twoMove);

        int oneDistance = oneMove.First.Y - oneBegin.First.Y;
        int twoDistance = twoMove.First.Y - twoBegin.First.Y;
        Assert.True(twoDistance > oneDistance);
        Assert.InRange(twoDistance, 1, 360);
    }

    [Fact]
    public void DirectionChangeLiftsBeforeStartingTheOppositeGesture()
    {
        var gesture = new PrecisionScrollGesture();
        gesture.AddDetent(1, 1, 0);
        gesture.TryAdvance(0, out _);
        gesture.TryAdvance(10, out _);

        gesture.AddDetent(-1, 1, 11);

        Assert.True(gesture.TryAdvance(11, out var end));
        Assert.Equal(PrecisionScrollFrameKind.End, end.Kind);
        Assert.True(gesture.TryAdvance(11, out var begin));
        Assert.Equal(PrecisionScrollFrameKind.Begin, begin.Kind);
        Assert.Equal(5_000, begin.First.Y);
    }

    [Theory]
    [InlineData(WheelAxis.Vertical, 120, WheelEventSource.MessageHook, false, true)]
    [InlineData(WheelAxis.Vertical, -120, WheelEventSource.MessageHook, false, true)]
    [InlineData(WheelAxis.Vertical, 40, WheelEventSource.MessageHook, false, false)]
    [InlineData(WheelAxis.Horizontal, 120, WheelEventSource.MessageHook, false, false)]
    [InlineData(WheelAxis.Vertical, 120, WheelEventSource.RawInput, false, false)]
    [InlineData(WheelAxis.Vertical, 120, WheelEventSource.MessageHook, true, false)]
    public void OnlyClassicPhysicalVerticalDetentsAreConverted(
        WheelAxis axis,
        short delta,
        WheelEventSource source,
        bool injected,
        bool expected)
    {
        var wheelEvent = new MouseWheelEvent(
            axis,
            delta,
            TimestampMs: 0,
            Device: IntPtr.Zero,
            source,
            IsInjected: injected);

        Assert.Equal(expected, PrecisionScrollEngine.CanConvert(in wheelEvent));
    }

    private static List<PrecisionScrollFrame> AdvanceUntilEnded(
        PrecisionScrollGesture gesture,
        int startMs)
    {
        var frames = new List<PrecisionScrollFrame>();
        for (int now = startMs; now <= 500 && gesture.IsActive; now += 10)
        {
            if (gesture.TryAdvance(now, out var frame))
                frames.Add(frame);
        }
        return frames;
    }
}
