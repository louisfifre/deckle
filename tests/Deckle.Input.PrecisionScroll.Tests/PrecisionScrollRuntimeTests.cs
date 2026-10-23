using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

// The worker used to end on its own (a refused frame, a full queue, a fault) and
// leave the wheel native for the rest of the session, while the app still believed
// it ran and the switch stayed on. The runtime now restarts it once, and when that
// does not hold it says the wheel stays native until the person toggles the switch.
// The engine, the host and the clock are fakes: the real engine creates a synthetic
// touchpad device and the real delay takes seconds.
[Trait("Category", "observability")]
[Collection(PrecisionScrollObservabilityCollection.Name)]
public sealed class PrecisionScrollRuntimeTests
{
    [Fact]
    [Trait("Category", "regression")]
    public async Task AWorkerThatEndsOnItsOwnIsRestartedOnceAfterTheDelay()
    {
        using var h = new Harness();
        using var listener = new TestEventListener("Deckle-PrecisionScroll");
        h.Runtime.Apply(true, null);

        h.Engines[0].End("injection_failed");
        await h.Runtime.Handling;

        // The wheel waits out the delay before anything starts again.
        Assert.Single(h.Engines);
        Assert.Equal(PrecisionScrollRuntime.RestartDelay, Assert.Single(h.RequestedDelays));
        Assert.True(h.Engines[0].Disposed);

        h.ReleaseDelay(0);
        await h.Runtime.Restarting;

        Assert.Equal(2, h.Engines.Count);
        Assert.True(h.Engines[1].Started);
        Assert.DoesNotContain(listener.Events, e => e.EventId == DecklePrecisionScrollSource.EvtFellBackToNativeWheel);
        // The restart is a Verbose detail: the engine's own lines told why it ended.
        EventWrittenEventArgs scheduled = Assert.Single(
            listener.Events, e => e.EventId == DecklePrecisionScrollSource.EvtRestartScheduledDetail);
        Assert.Equal(EventLevel.Verbose, scheduled.Level);
        Assert.Equal("injection_failed", scheduled.Payload![0]);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AWorkerThatStopsAgainRightAfterTheRestartStaysDownAndSaysWhy()
    {
        using var h = new Harness();
        using var listener = new TestEventListener("Deckle-PrecisionScroll");
        h.Runtime.Apply(true, null);
        await h.RestartAfter(0, "injection_failed");

        h.ClockMs += 1_000;
        h.Engines[1].End("queue_full");
        await h.Runtime.Handling;

        // No third attempt: one Warning names the reason, the wheel is native.
        Assert.Equal(2, h.Engines.Count);
        Assert.Single(h.RequestedDelays);
        EventWrittenEventArgs fellBack = Assert.Single(
            listener.Events, e => e.EventId == DecklePrecisionScrollSource.EvtFellBackToNativeWheel);
        Assert.Equal(EventLevel.Warning, fellBack.Level);
        Assert.Equal(ObservationTags.Milestone, fellBack.Tags & ObservationTags.Milestone);
        Assert.Equal("queue_full", fellBack.Payload![0]);
        Assert.True(h.Engines[1].Disposed);
        Assert.Equal(h.HostStarts, h.HostStops);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task AStopAfterALongRunIsANewFirstStopAndEarnsItsOwnRestart()
    {
        using var h = new Harness();
        using var listener = new TestEventListener("Deckle-PrecisionScroll");
        h.Runtime.Apply(true, null);
        await h.RestartAfter(0, "worker_faulted");

        h.ClockMs += (long)PrecisionScrollRuntime.StableRun.TotalMilliseconds;
        await h.RestartAfter(1, "queue_full");

        Assert.Equal(3, h.Engines.Count);
        Assert.Equal(2, h.RequestedDelays.Count);
        Assert.DoesNotContain(listener.Events, e => e.EventId == DecklePrecisionScrollSource.EvtFellBackToNativeWheel);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ARestartThatCannotStartStaysDownWithItsOwnReason()
    {
        using var h = new Harness();
        using var listener = new TestEventListener("Deckle-PrecisionScroll");
        h.Runtime.Apply(true, null);
        h.NextEngineStarts = false;

        h.Engines[0].End("injection_failed");
        await h.Runtime.Handling;
        h.ReleaseDelay(0);
        await h.Runtime.Restarting;

        EventWrittenEventArgs fellBack = Assert.Single(
            listener.Events, e => e.EventId == DecklePrecisionScrollSource.EvtFellBackToNativeWheel);
        Assert.Equal("restart_failed", fellBack.Payload![0]);
        Assert.Equal(2, h.Engines.Count);
        Assert.Equal(h.HostStarts, h.HostStops);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task TogglingTheSwitchClosesTheIncidentOnceAndGivesTheRestartBack()
    {
        using var h = new Harness();
        using var listener = new TestEventListener("Deckle-PrecisionScroll");
        h.Runtime.Apply(true, null);
        await h.RestartAfter(0, "injection_failed");
        h.Engines[1].End("injection_failed");
        await h.Runtime.Handling;

        h.Runtime.Apply(false, null);
        h.Runtime.Apply(true, null);

        EventWrittenEventArgs recovered = Assert.Single(
            listener.Events, e => e.EventId == DecklePrecisionScrollSource.EvtFallbackRecovered);
        Assert.Equal(EventLevel.Informational, recovered.Level);
        Assert.Equal(ObservationTags.Milestone, recovered.Tags & ObservationTags.Milestone);
        Assert.Equal("injection_failed", recovered.Payload![0]);

        // Switched back on: running again, and a new stop restarts again.
        Assert.Equal(3, h.Engines.Count);
        Assert.True(h.Engines[2].Started);
        await h.RestartAfter(2, "queue_full");
        Assert.Equal(4, h.Engines.Count);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task ChangingASettingWhileTheWheelIsNativeDoesNotRestartTheEngine()
    {
        using var h = new Harness();
        h.Runtime.Apply(true, null);

        // Waiting out the delay.
        h.Engines[0].End("queue_full");
        await h.Runtime.Handling;
        h.Runtime.Apply(true, new PrecisionScrollTuning());
        Assert.Single(h.Engines);

        // Down for good.
        h.ReleaseDelay(0);
        await h.Runtime.Restarting;
        h.Engines[1].End("queue_full");
        await h.Runtime.Handling;
        h.Runtime.Apply(true, new PrecisionScrollTuning());
        Assert.Equal(2, h.Engines.Count);
    }

    [Fact]
    public async Task SwitchingOffWhileTheRestartWaitsCancelsIt()
    {
        using var h = new Harness();
        h.Runtime.Apply(true, null);
        h.Engines[0].End("queue_full");
        await h.Runtime.Handling;

        h.Runtime.Apply(false, null);
        h.ReleaseDelay(0);
        await h.Runtime.Restarting;

        Assert.Single(h.Engines);
        Assert.Equal(h.HostStarts, h.HostStops);
    }

    [Fact]
    public async Task AStopWeAskedForIsNotAnEndOnItsOwn()
    {
        using var h = new Harness();
        h.Runtime.Apply(true, null);
        h.Runtime.Apply(false, null);

        // The engine of a switched-off feature raising late is stale news.
        h.Engines[0].End("queue_full");
        await h.Runtime.Handling;

        Assert.Single(h.Engines);
        Assert.Empty(h.RequestedDelays);
    }

    [Fact]
    public void TheLogKeepsTheStayOffIncidentAndItsRecoveryAndNotTheRestartDetail()
    {
        var declared = ProviderManifest.EventsOf(typeof(DecklePrecisionScrollSource));

        var fellBack = declared.Single(e => e.Id == DecklePrecisionScrollSource.EvtFellBackToNativeWheel);
        var recovered = declared.Single(e => e.Id == DecklePrecisionScrollSource.EvtFallbackRecovered);
        var detail = declared.Single(e => e.Id == DecklePrecisionScrollSource.EvtRestartScheduledDetail);

        Assert.Equal(EventLevel.Warning, fellBack.Level);
        Assert.Equal(ObservationTags.Milestone, fellBack.Tags & ObservationTags.Milestone);
        Assert.Equal(ObservationTags.Milestone, recovered.Tags & ObservationTags.Milestone);
        Assert.Equal(EventLevel.Verbose, detail.Level);
        Assert.False(LoggedPayloads.Travels(detail));
    }

    private sealed class Harness : IDisposable
    {
        private readonly List<TaskCompletionSource> _delays = [];

        public Harness()
        {
            Runtime = new PrecisionScrollRuntime(
                startHost: () => { HostStarts++; return true; },
                stopHost: () => HostStops++,
                createEngine: () =>
                {
                    var engine = new FakeEngine(NextEngineStarts);
                    NextEngineStarts = true;
                    Engines.Add(engine);
                    return engine;
                },
                reportUnavailable: _ => { },
                delay: (wait, ct) =>
                {
                    RequestedDelays.Add(wait);
                    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _delays.Add(gate);
                    ct.Register(() => gate.TrySetCanceled(ct));
                    return gate.Task;
                },
                nowMs: () => ClockMs);
        }

        public PrecisionScrollRuntime Runtime { get; }
        public List<FakeEngine> Engines { get; } = [];
        public List<TimeSpan> RequestedDelays { get; } = [];
        public long ClockMs { get; set; }
        public int HostStarts { get; private set; }
        public int HostStops { get; private set; }
        public bool NextEngineStarts { get; set; } = true;

        public void ReleaseDelay(int index) => _delays[index].TrySetResult();

        // Ends engine `engineIndex` on its own, lets the delay pass and waits for
        // the restart to be in place.
        public async Task RestartAfter(int engineIndex, string reason)
        {
            int delays = RequestedDelays.Count;
            Engines[engineIndex].End(reason);
            await Runtime.Handling;
            ReleaseDelay(delays);
            await Runtime.Restarting;
        }

        public void Dispose() => Runtime.Dispose();
    }

    private sealed class FakeEngine(bool starts) : IPrecisionScrollEngine
    {
        public event Action<string>? EndedOnItsOwn;

        public bool Started { get; private set; }
        public bool Disposed { get; private set; }

        public bool Start() => Started = starts;

        public void SetTuning(PrecisionScrollTuning tuning) { }

        // What the real worker does as its last act, from its own thread.
        public void End(string reason) => EndedOnItsOwn?.Invoke(reason);

        public void Dispose() => Disposed = true;
    }
}
