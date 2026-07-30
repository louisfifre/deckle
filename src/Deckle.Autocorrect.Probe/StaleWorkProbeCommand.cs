using System.Diagnostics;
using System.Text.Json;
using Deckle.Input;

namespace Deckle.Autocorrect.Probe;

// Controlled experiment for stale single-flight work. It uses the production
// coordinator and background lane with a blocking in-memory judge: no model,
// target window, or text injection participates. The probe measures whether a
// newly eligible sentence can reach the judge while an invalidated judgment is
// still occupying the lane/coordinator ownership slot.
internal static class StaleWorkProbeCommand
{
    private const int StaleHoldMilliseconds = 250;

    public static int Run(ProbeArguments parsed)
    {
        var baseline = new double[parsed.Iterations];
        var staleBlocked = new double[parsed.Iterations];
        for (int iteration = 0; iteration < parsed.Iterations; iteration++)
        {
            baseline[iteration] = RunBaseline();
            staleBlocked[iteration] = RunStaleTrial();
        }

        var report = new StaleWorkProbeReport(
            parsed.Iterations,
            StaleHoldMilliseconds,
            parsed.Iterations,
            Stopwatch.Frequency,
            baseline,
            staleBlocked,
            MetricDistribution.Create(baseline),
            MetricDistribution.Create(staleBlocked));
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        }));
        return 0;
    }

    private static double RunBaseline()
    {
        using var judge = new ControlledWholeSentenceJudge(blockFirst: false);
        using var host = new ProbeKeyboardHost();
        using var lane = new BackgroundRerankLane(judge, host);
        using var coordinator = CreateCoordinator(lane);

        long eligible = FeedClosedSentence(coordinator);
        AssertSignal(judge.FirstEntered, "baseline judge entry");
        return ElapsedMilliseconds(eligible, judge.FirstEnteredTick);
    }

    private static double RunStaleTrial()
    {
        using var judge = new ControlledWholeSentenceJudge(blockFirst: true);
        using var host = new ProbeKeyboardHost();
        using var lane = new BackgroundRerankLane(judge, host);
        using var coordinator = CreateCoordinator(lane);

        FeedClosedSentence(coordinator);
        AssertSignal(judge.FirstEntered, "first stale judge entry");
        coordinator.Invalidate(ResetReason.Enter);
        long secondEligible = FeedClosedSentence(coordinator);

        Thread.Sleep(StaleHoldMilliseconds);
        judge.ReleaseFirst.Set();
        AssertSignal(host.DrainRequestedSignal, "stale verdict drain request");
        host.Drain();
        AssertSignal(judge.SecondEntered, "second judge entry");
        return ElapsedMilliseconds(secondEligible, judge.SecondEnteredTick);
    }

    private static SentenceRerankCoordinator CreateCoordinator(IRerankLane lane)
    {
        var probe = new ProbeAmbiguity();
        return new SentenceRerankCoordinator(
            lane,
            probe,
            new RejectingInjector(),
            static () => string.Empty,
            wholeSentenceProbe: probe);
    }

    private static long FeedClosedSentence(SentenceRerankCoordinator coordinator)
    {
        coordinator.OnWordCommitted("Il", ' ', true);
        coordinator.OnWordCommitted("y", ' ', true);
        coordinator.OnWordCommitted("a", ' ', true);
        coordinator.OnWordCommitted("une", ' ', true);
        coordinator.OnWordCommitted("seul", ' ', true);
        long eligible = Stopwatch.GetTimestamp();
        coordinator.OnWordCommitted("erreur", '.', true);
        return eligible;
    }

    private static void AssertSignal(ManualResetEventSlim signal, string name)
    {
        if (!signal.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException($"Timed out waiting for {name}.");
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1000.0 / Stopwatch.Frequency;

    private sealed class ControlledWholeSentenceJudge(bool blockFirst)
        : ISentenceReranker, IWholeSentenceReranker, IDisposable
    {
        private int _calls;

        public ManualResetEventSlim FirstEntered { get; } = new(false);
        public ManualResetEventSlim SecondEntered { get; } = new(false);
        public ManualResetEventSlim ReleaseFirst { get; } = new(!blockFirst);
        public long FirstEnteredTick { get; private set; }
        public long SecondEnteredTick { get; private set; }

        public RerankOutcome Rerank(
            IReadOnlyList<string> sentence,
            int slotIndex,
            IReadOnlyList<AccentVariant> candidates) =>
            throw new InvalidOperationException("Expected a global sentence request.");

        public RerankOutcome RerankSentence(ClosedSentenceTransaction transaction)
        {
            int call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                FirstEnteredTick = Stopwatch.GetTimestamp();
                FirstEntered.Set();
                if (blockFirst)
                    ReleaseFirst.Wait();
            }
            else if (call == 2)
            {
                SecondEnteredTick = Stopwatch.GetTimestamp();
                SecondEntered.Set();
            }

            if (call == 1)
            {
                return new RerankOutcome(
                    "seule",
                    Array.Empty<RerankCandidateScore>(),
                    Margin: 2.0,
                    Threshold: 1.0,
                    AbstainReason: null)
                {
                    ChosenSlotIndex = 4,
                };
            }

            return RerankOutcome.Abstained(RerankOutcome.AbstainReasons.NoRule);
        }

        public void Dispose()
        {
            ReleaseFirst.Set();
            FirstEntered.Dispose();
            SecondEntered.Dispose();
            ReleaseFirst.Dispose();
        }
    }

    private sealed class ProbeAmbiguity : IAmbiguityProbe
    {
        private static readonly AccentVariant[] Candidates =
        [
            new AccentVariant("seul", 100),
            new AccentVariant("seule", 90),
        ];

        public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
            string.Equals(word, "seul", StringComparison.OrdinalIgnoreCase)
                ? Candidates
                : Array.Empty<AccentVariant>();

        public IReadOnlyList<AccentVariant> SentenceCandidates(
            string word,
            bool includeTypedLiteral) => AmbiguousCandidates(word);
    }

    private sealed class RejectingInjector : ITextInjector
    {
        public bool Replace(string current, string target) =>
            throw new InvalidOperationException("The stale-work probe never authorizes an edit.");
    }

    private sealed class ProbeKeyboardHost : IKeyboardInputHost, IDisposable
    {
        private int _pendingDrains;

        public event Action<KeyboardKeyEvent>? KeyReceived
        {
            add { }
            remove { }
        }

        public event Action? PointerInteraction
        {
            add { }
            remove { }
        }

        public event Action<MouseWheelEvent>? WheelObserved
        {
            add { }
            remove { }
        }

        public event Action? FocusChanged
        {
            add { }
            remove { }
        }

        public event Action? DrainRequested;

        public ManualResetEventSlim DrainRequestedSignal { get; } = new(false);

        public bool Start() => true;
        public void Stop() { }
        public void SetWheelInterceptor(IWheelInterceptor? interceptor) { }

        public void RequestDrain()
        {
            Interlocked.Increment(ref _pendingDrains);
            DrainRequestedSignal.Set();
        }

        public void Drain()
        {
            while (Interlocked.Exchange(ref _pendingDrains, 0) > 0)
                DrainRequested?.Invoke();
            DrainRequestedSignal.Reset();
        }

        public void Dispose() => DrainRequestedSignal.Dispose();
    }

    private sealed record StaleWorkProbeReport(
        int Iterations,
        int StaleHoldMilliseconds,
        int StaleChangingVerdictsTested,
        long StopwatchFrequency,
        IReadOnlyList<double> BaselineMilliseconds,
        IReadOnlyList<double> StaleBlockedMilliseconds,
        MetricDistribution Baseline,
        MetricDistribution StaleBlocked);
}
