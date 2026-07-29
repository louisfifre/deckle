using System.Reflection;
using System.Text.Json;
using Deckle.Input.PrecisionScroll;
using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

[Trait("Category", "unit")]
public sealed class PerceptualBaselineTests
{
    [Fact]
    public void NamedScenariosPreserveTheAcceptedPerceptualContracts()
    {
        BaselineFixture fixture = LoadFixture();
        Assert.Equal(1, fixture.Schema);
        Assert.Equal("5c0a75ae55c457b628b08ddf74f455c9a52bae21", fixture.ReferenceCommit);

        var results = new Dictionary<string, ReplayResult>();
        foreach (BaselineScenario scenario in fixture.Scenarios)
        {
            ReplayResult replay = Replay(scenario);
            results.Add(scenario.Name, replay);
            AssertScenario(scenario, replay);
        }

        foreach (BaselineScenario scenario in fixture.Scenarios)
        {
            BaselineExpected expected = scenario.Expected;
            if (expected.FasterThan is null)
                continue;

            Assert.True(
                results[scenario.Name].PeakSpeed
                    > results[expected.FasterThan].PeakSpeed * expected.MinimumSpeedRatio,
                $"{scenario.Name} no longer preserves its cadence advantage.");
        }
    }

    private static void AssertScenario(BaselineScenario scenario, ReplayResult replay)
    {
        BaselineExpected expected = scenario.Expected;
        if (expected.BeginCount is int beginCount)
            Assert.Equal(beginCount, replay.Count(PrecisionScrollFrameKind.Begin));
        if (expected.EndCount is int endCount)
            Assert.Equal(endCount, replay.Count(PrecisionScrollFrameKind.End));
        if (expected.MinimumBeginCount is int minimumBeginCount)
            Assert.True(replay.Count(PrecisionScrollFrameKind.Begin) >= minimumBeginCount);
        if (expected.BalancedContacts)
        {
            Assert.Equal(
                replay.Count(PrecisionScrollFrameKind.Begin),
                replay.Count(PrecisionScrollFrameKind.End));
        }
        if (expected.TravelMm is double travelMm)
            Assert.InRange(replay.TotalTravel / 100, travelMm - 0.01, travelMm + 0.01);
        if (expected.Rollover is bool rollover)
            Assert.Equal(rollover, replay.Frames.Any(item => item.Frame.IsRollover));
        if (expected.StationaryBeforeEnd)
            Assert.True(replay.HasStationaryFrameBeforeEnd);

        if (expected.MotionAfterMs is int motionAfterMs)
        {
            double[] movements = replay.SignedMovementsAfter(motionAfterMs).ToArray();
            Assert.NotEmpty(movements);
            if (expected.MotionDirection == "negative-only")
            {
                Assert.Contains(movements, movement => movement < 0);
                Assert.All(movements, movement => Assert.True(movement <= 0));
            }
        }
    }

    private static ReplayResult Replay(BaselineScenario scenario)
    {
        BaselineInput[] inputs = scenario.Inputs
            ?? Enumerable.Range(0, scenario.Series!.Count)
                .Select(index => new BaselineInput(
                    scenario.Series.StartMs + index * scenario.Series.IntervalMs,
                    scenario.Series.Detents))
                .ToArray();

        var gesture = new PrecisionScrollGesture();
        var frames = new List<TimedFrame>();
        int nextInput = 0;
        for (int now = 0; now <= scenario.UntilMs; now += PrecisionScrollGesture.FrameIntervalMs)
        {
            while (nextInput < inputs.Length && inputs[nextInput].AtMs <= now)
            {
                BaselineInput input = inputs[nextInput++];
                gesture.AddDetents(input.Detents, input.AtMs);
            }

            while (gesture.TryAdvance(now, out PrecisionScrollFrame frame))
                frames.Add(new TimedFrame(now, frame));
        }

        return new ReplayResult(frames);
    }

    private static BaselineFixture LoadFixture()
    {
        Assembly assembly = typeof(PerceptualBaselineTests).Assembly;
        string resource = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith("precision-scroll-baseline.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<BaselineFixture>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private sealed record BaselineFixture(
        int Schema,
        string ReferenceCommit,
        BaselineScenario[] Scenarios);

    private sealed record BaselineScenario(
        string Name,
        BaselineInput[]? Inputs,
        BaselineSeries? Series,
        int UntilMs,
        BaselineExpected Expected);

    private sealed record BaselineInput(int AtMs, int Detents);

    private sealed record BaselineSeries(
        int StartMs,
        int IntervalMs,
        int Count,
        int Detents);

    private sealed record BaselineExpected(
        int? BeginCount = null,
        int? EndCount = null,
        int? MinimumBeginCount = null,
        bool BalancedContacts = false,
        double? TravelMm = null,
        bool? Rollover = null,
        bool StationaryBeforeEnd = false,
        int? MotionAfterMs = null,
        string? MotionDirection = null,
        string? FasterThan = null,
        double MinimumSpeedRatio = 0);

    private readonly record struct TimedFrame(int AtMs, PrecisionScrollFrame Frame);

    private sealed class ReplayResult(List<TimedFrame> frames)
    {
        public List<TimedFrame> Frames { get; } = frames;

        public double TotalTravel => Segments().Sum(segment => Math.Abs(segment.Movement));

        public double PeakSpeed => Segments()
            .Select(segment => Math.Abs(segment.Movement) * 1000 / segment.ElapsedMs)
            .DefaultIfEmpty(0)
            .Max();

        public bool HasStationaryFrameBeforeEnd
        {
            get
            {
                for (int index = 2; index < Frames.Count; index++)
                {
                    if (Frames[index].Frame.Kind == PrecisionScrollFrameKind.End
                        && Frames[index - 1].Frame.Kind == PrecisionScrollFrameKind.Move
                        && Frames[index - 2].Frame.Kind == PrecisionScrollFrameKind.Move
                        && Frames[index - 1].Frame.First.Y == Frames[index - 2].Frame.First.Y)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

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

        private IEnumerable<(double Movement, int ElapsedMs)> Segments()
        {
            TimedFrame? previous = null;
            foreach (TimedFrame current in Frames)
            {
                if (current.Frame.Kind == PrecisionScrollFrameKind.Move
                    && previous is { } before
                    && current.AtMs > before.AtMs)
                {
                    yield return (
                        current.Frame.First.Y - before.Frame.First.Y,
                        current.AtMs - before.AtMs);
                }

                previous = current.Frame.Kind == PrecisionScrollFrameKind.End
                    ? null
                    : current;
            }
        }
    }
}
