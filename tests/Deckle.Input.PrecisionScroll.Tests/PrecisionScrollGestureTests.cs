using Deckle.Input;
using Deckle.Input.PrecisionScroll;
using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

[Trait("Category", "unit")]
public sealed class PrecisionScrollGestureTests
{
    [Fact]
    public void OneDetentProducesOneContinuousRecognizableGesture()
    {
        var tuning = new PrecisionScrollTuning();

        ReplayResult replay = Replay([new(0, 1)], tuning: tuning);

        Assert.Equal(1, replay.Count(PrecisionScrollFrameKind.Begin));
        Assert.Equal(1, replay.Count(PrecisionScrollFrameKind.End));
        double expectedTravel = tuning.DistancePerDetentMm * 100;
        Assert.InRange(
            replay.TotalTravel,
            expectedTravel - 1,
            expectedTravel + 1);
    }

    [Fact]
    public void HeldSlowInputStaysInOneContactAndSettlesBeforeLift()
    {
        ReplayResult replay = Replay(
            Enumerable.Range(0, 7).Select(index => new WheelInput(index * 100, 1)).ToArray());

        Assert.Equal(1, replay.Count(PrecisionScrollFrameKind.Begin));
        Assert.Equal(1, replay.Count(PrecisionScrollFrameKind.End));
        Assert.DoesNotContain(replay.Frames, item => item.Frame.IsRollover);
        Assert.True(replay.TerminalToPeakSpeedRatio < 0.2);
    }

    [Fact]
    public void FastCadenceMovesFasterAndFartherThanSlowCadence()
    {
        ReplayResult weak = Replay([new(0, 1), new(70, 1)]);
        ReplayResult strong = Replay(
            Enumerable.Range(0, 16).Select(index => new WheelInput(index * 8, 1)).ToArray());

        Assert.True(strong.PeakSpeed > weak.PeakSpeed * 3);
        Assert.True(strong.TotalTravel > weak.TotalTravel * 3);
        Assert.Equal(0, strong.TerminalSpeed);
        Assert.Equal(0, weak.TerminalSpeed);
    }

    [Fact]
    public void LongFreeSpinUsesNativeRolloversWithoutDroppingTravel()
    {
        ReplayResult replay = Replay(
            Enumerable.Range(0, 200).Select(index => new WheelInput(index * 8, 1)).ToArray(),
            untilMs: 2_200);

        Assert.True(replay.Count(PrecisionScrollFrameKind.Begin) > 1);
        Assert.Contains(replay.Frames, item => item.Frame.IsRollover);
        Assert.True(replay.TotalTravel > PrecisionTouchpadInjector.DeviceHeight * 4);
        Assert.Equal(
            replay.Count(PrecisionScrollFrameKind.Begin),
            replay.Count(PrecisionScrollFrameKind.End));
    }

    [Fact]
    public void DirectionChangeReversesTheHeldContactWithoutOldDirectionMotion()
    {
        ReplayResult replay = Replay([new(0, 1), new(30, 1), new(61, -1)]);

        Assert.Equal(1, replay.Count(PrecisionScrollFrameKind.Begin));
        Assert.Equal(1, replay.Count(PrecisionScrollFrameKind.End));
        double[] movements = replay.SignedMovementsAfter(70).ToArray();
        Assert.Contains(movements, movement => movement < 0);
        Assert.All(movements, movement => Assert.True(movement <= 0));
    }

    [Fact]
    public void GestureUsesTheWholeAvailableVerticalSurface()
    {
        ReplayResult positive = Replay([new(0, 1)]);
        ReplayResult negative = Replay([new(0, -1)]);

        Assert.Equal(0, positive.Frames[0].Frame.First.Y);
        Assert.Equal(
            PrecisionTouchpadInjector.DeviceHeight - 1,
            negative.Frames[0].Frame.First.Y);
        Assert.All(
            positive.Frames.Concat(negative.Frames),
            item => Assert.InRange(
                item.Frame.First.Y,
                0,
                PrecisionTouchpadInjector.DeviceHeight - 1));
    }

    [Fact]
    public void ElapsedTimeMakesSchedulerJitterNearlyEquivalent()
    {
        WheelInput[] input = Enumerable.Range(0, 10)
            .Select(index => new WheelInput(index * 40, 1))
            .ToArray();

        ReplayResult regular = Replay(input, pollIntervalMs: 10);
        ReplayResult jittered = Replay(input, pollIntervalMs: 17);

        Assert.InRange(jittered.TotalTravel / regular.TotalTravel, 0.9, 1.1);
        Assert.InRange(jittered.PeakSpeed / regular.PeakSpeed, 0.8, 1.2);
    }

    [Fact]
    public void DistancePerDetentChangesMagnitudeWithoutChangingReleaseKind()
    {
        WheelInput[] input = [new(0, 1), new(100, 1), new(200, 1)];
        var defaults = new PrecisionScrollTuning();

        ReplayResult shortTravel = Replay(
            input,
            tuning: defaults with { DistancePerDetentMm = 0.75 });
        ReplayResult longTravel = Replay(
            input,
            tuning: defaults with { DistancePerDetentMm = 2.25 });

        Assert.True(longTravel.TotalTravel > shortTravel.TotalTravel * 2.5);
        Assert.True(shortTravel.TerminalToPeakSpeedRatio < 0.2);
        Assert.True(longTravel.TerminalToPeakSpeedRatio < 0.2);
        Assert.Equal(
            shortTravel.Count(PrecisionScrollFrameKind.End),
            longTravel.Count(PrecisionScrollFrameKind.End));
    }

    [Fact]
    public void InitialStepDurationChangesOnlyTheUnmeasuredFirstStepPace()
    {
        WheelInput[] input = [new(0, 1)];
        var defaults = new PrecisionScrollTuning();

        ReplayResult direct = Replay(
            input,
            tuning: defaults with { InitialStepDurationMs = 30 });
        ReplayResult gentle = Replay(
            input,
            tuning: defaults with { InitialStepDurationMs = 120 });

        Assert.True(direct.PeakSpeed > gentle.PeakSpeed * 3);
        Assert.InRange(gentle.TotalTravel / direct.TotalTravel, 0.99, 1.01);
    }

    [Fact]
    public void ReleaseTimingChangesContactDurationWithoutAddingTravel()
    {
        WheelInput[] input = [new(0, 1), new(40, 1), new(80, 1)];
        var defaults = new PrecisionScrollTuning();

        ReplayResult early = Replay(
            input,
            tuning: defaults with { QuietPeriodScale = 1 });
        ReplayResult settled = Replay(
            input,
            tuning: defaults with { QuietPeriodScale = 4 });

        Assert.True(settled.EndAtMs > early.EndAtMs + 80);
        Assert.InRange(settled.TotalTravel / early.TotalTravel, 0.99, 1.01);
        Assert.Equal(0, early.TerminalSpeed);
        Assert.Equal(0, settled.TerminalSpeed);
    }

    [Theory]
    [InlineData(WheelAxis.Vertical, 120, WheelEventSource.MessageHook, false, true)]
    [InlineData(WheelAxis.Vertical, -120, WheelEventSource.MessageHook, false, true)]
    [InlineData(WheelAxis.Vertical, 240, WheelEventSource.MessageHook, false, true)]
    [InlineData(WheelAxis.Vertical, -360, WheelEventSource.MessageHook, false, true)]
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
            IsInjected: injected,
            HasEquivalentTarget: true);

        Assert.Equal(expected, PrecisionScrollEngine.CanConvert(in wheelEvent));
    }

    [Theory]
    [InlineData(WheelInputState.Shift)]
    [InlineData(WheelInputState.Control)]
    [InlineData(WheelInputState.Alt)]
    [InlineData(WheelInputState.LeftButton)]
    public void ModifiedWheelInputRemainsNative(WheelInputState inputState)
    {
        var wheelEvent = new MouseWheelEvent(
            WheelAxis.Vertical,
            Delta: 120,
            TimestampMs: 0,
            Device: IntPtr.Zero,
            WheelEventSource.MessageHook,
            InputState: inputState,
            HasEquivalentTarget: true);

        Assert.False(PrecisionScrollEngine.CanConvert(in wheelEvent));
    }

    [Fact]
    public void WheelInputWithADifferentPointerTargetRemainsNative()
    {
        var wheelEvent = new MouseWheelEvent(
            WheelAxis.Vertical,
            Delta: 120,
            TimestampMs: 0,
            Device: IntPtr.Zero,
            WheelEventSource.MessageHook,
            HasEquivalentTarget: false);

        Assert.False(PrecisionScrollEngine.CanConvert(in wheelEvent));
    }

    private static ReplayResult Replay(
        WheelInput[] input,
        int pollIntervalMs = 10,
        int untilMs = 1_500,
        PrecisionScrollTuning? tuning = null)
    {
        var gesture = new PrecisionScrollGesture(tuning);
        var frames = new List<TimedFrame>();
        int nextInput = 0;

        for (int now = 0; now <= untilMs; now += pollIntervalMs)
        {
            while (nextInput < input.Length && input[nextInput].AtMs <= now)
            {
                WheelInput wheel = input[nextInput++];
                gesture.AddDetents(wheel.Detents, wheel.AtMs);
            }

            while (gesture.TryAdvance(now, out PrecisionScrollFrame frame))
                frames.Add(new TimedFrame(now, frame));
        }

        return new ReplayResult(frames);
    }

    private readonly record struct WheelInput(int AtMs, int Detents);
    private readonly record struct TimedFrame(int AtMs, PrecisionScrollFrame Frame);

    private sealed class ReplayResult(List<TimedFrame> frames)
    {
        public List<TimedFrame> Frames { get; } = frames;

        public double TotalTravel => Segments().Sum(segment => segment.Distance);

        public double PeakSpeed => Segments().Select(segment => segment.Speed).DefaultIfEmpty(0).Max();

        public int EndAtMs => Frames.Last(item =>
            item.Frame.Kind == PrecisionScrollFrameKind.End).AtMs;

        public double TerminalSpeed => Segments()
            .Where(segment => segment.Terminal)
            .Select(segment => segment.Speed)
            .LastOrDefault();

        public double TerminalToPeakSpeedRatio => PeakSpeed == 0 ? 0 : TerminalSpeed / PeakSpeed;

        public int Count(PrecisionScrollFrameKind kind) =>
            Frames.Count(item => item.Frame.Kind == kind);

        public IEnumerable<double> SignedMovementsAfter(int timestampMs)
        {
            TimedFrame? previous = null;
            foreach (TimedFrame current in Frames)
            {
                if (current.Frame.Kind == PrecisionScrollFrameKind.Move
                    && current.AtMs >= timestampMs
                    && previous is { } before)
                {
                    yield return current.Frame.First.Y - before.Frame.First.Y;
                }

                previous = current.Frame.Kind == PrecisionScrollFrameKind.End
                    ? null
                    : current;
            }
        }

        private IEnumerable<MotionSegment> Segments()
        {
            TimedFrame? previous = null;
            for (int index = 0; index < Frames.Count; index++)
            {
                TimedFrame current = Frames[index];
                if (current.Frame.Kind == PrecisionScrollFrameKind.Move && previous is { } before)
                {
                    int elapsed = current.AtMs - before.AtMs;
                    if (elapsed > 0)
                    {
                        double distance = Math.Abs(current.Frame.First.Y - before.Frame.First.Y);
                        bool terminal = index + 1 < Frames.Count
                            && Frames[index + 1].Frame.Kind == PrecisionScrollFrameKind.End;
                        yield return new MotionSegment(
                            distance,
                            distance * 1000 / elapsed,
                            terminal);
                    }
                }

                previous = current.Frame.Kind == PrecisionScrollFrameKind.End
                    ? null
                    : current;
            }
        }
    }

    private readonly record struct MotionSegment(
        double Distance,
        double Speed,
        bool Terminal);
}
