using System.Diagnostics;
using Xunit;

namespace Deckle.Orchestration.Tests;

// What a person gets from the rules of a listed app: they apply when the app
// comes to the front, and they are given back when the app quits, when it is
// disabled or removed, and when Deckle stops. The runtime is driven through
// its constructor's seams (a fake foreground monitor, fake effects); the
// registry is the real service on an isolated data root, and the processes
// are real children, because the exit watch waits on a real process handle.
[Collection(OrchestrationObservabilityCollection.Name)]
[Trait("Category", "unit")]
public sealed class RuntimeRulesTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly OrchestrationSettingsService _settings = OrchestrationSettingsService.Instance;
    private readonly List<AppEntry> _listed = [];
    private readonly List<Process> _children = [];
    private readonly List<OrchestrationRuntime> _runtimes = [];

    public void Dispose()
    {
        foreach (OrchestrationRuntime runtime in _runtimes) runtime.Dispose();
        foreach (AppEntry entry in _listed) _settings.RemoveEntry(entry.Id);
        foreach (Process child in _children)
        {
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            child.Dispose();
        }
    }

    [Fact]
    public void AnAppComingToTheFrontHasItsRulesAppliedOnceHoweverOftenItIsSeen()
    {
        var effect = new RecordingEffect("hdr");
        FakeForeground foreground = new();
        AppEntry app = List("Hollow");
        OrchestrationRuntime runtime = Compose(foreground, [effect]);
        int pid = StartChild().Id;

        foreground.Raise(app, pid);
        foreground.Raise(app, pid);
        foreground.Raise(app, StartChild().Id);

        Assert.Equal(["apply hdr"], effect.Journal);
        Assert.Single(runtime.Sessions);
    }

    [Fact]
    public void TheRulesComeBackWhenTheAppQuits()
    {
        var effect = new RecordingEffect("hdr");
        FakeForeground foreground = new();
        AppEntry app = List("Hollow");
        OrchestrationRuntime runtime = Compose(foreground, [effect]);
        Process child = StartChild();
        foreground.Raise(app, child.Id);

        child.Kill();

        Assert.True(effect.WaitForRestore(Patience), "the rules were not given back after the app quit");
        Assert.Equal(["apply hdr", "restore hdr"], effect.Journal);
        Assert.Empty(runtime.Sessions);
    }

    [Fact]
    public void TheRulesStayUntilTheLastProcessOfTheAppQuits()
    {
        var effect = new RecordingEffect("hdr");
        FakeForeground foreground = new();
        AppEntry app = List("Hollow");
        OrchestrationRuntime runtime = Compose(foreground, [effect]);
        Process launcher = StartChild();
        Process game = StartChild();
        foreground.Raise(app, launcher.Id);
        foreground.Raise(app, game.Id);

        launcher.Kill();
        Assert.True(SpinWait.SpinUntil(() => runtime.Sessions.Single().ProcessIds.Count == 1, Patience));
        Assert.Equal(["apply hdr"], effect.Journal);

        game.Kill();
        Assert.True(effect.WaitForRestore(Patience), "the rules were not given back after the last process quit");
    }

    [Fact]
    public void TheRulesComeBackWhenTheAppIsDisabledWhileItRuns()
    {
        var effect = new RecordingEffect("hdr");
        FakeForeground foreground = new();
        AppEntry app = List("Hollow");
        Compose(foreground, [effect]);
        foreground.Raise(app, StartChild().Id);

        _settings.UpdateEntry(app.Id, e => e.Enabled = false);

        Assert.True(effect.WaitForRestore(Patience), "the rules were not given back after the app was disabled");
        Assert.Equal(["apply hdr", "restore hdr"], effect.Journal);
    }

    [Fact]
    public void TheRulesComeBackWhenTheAppIsRemovedFromTheListWhileItRuns()
    {
        var effect = new RecordingEffect("hdr");
        FakeForeground foreground = new();
        AppEntry app = List("Hollow");
        Compose(foreground, [effect]);
        foreground.Raise(app, StartChild().Id);

        _settings.RemoveEntry(app.Id);

        Assert.True(effect.WaitForRestore(Patience), "the rules were not given back after the app was removed");
    }

    [Fact]
    public void StoppingDeckleGivesBackTheRulesOfEveryRunningApp()
    {
        var effect = new RecordingEffect("hdr");
        FakeForeground foreground = new();
        AppEntry first = List("Hollow");
        AppEntry second = List("Celeste");
        OrchestrationRuntime runtime = Compose(foreground, [effect]);
        foreground.Raise(first, StartChild().Id);
        foreground.Raise(second, StartChild().Id);
        Assert.Equal(2, effect.Applied);

        runtime.Stop();

        // Synchronous: Deckle must not exit with a rule still applied.
        Assert.Equal(2, effect.Restored);
        Assert.Empty(runtime.Sessions);
    }

    [Fact]
    public void AnAppComingToTheFrontAfterDeckleStoppedGetsNoRule()
    {
        var effect = new RecordingEffect("hdr");
        FakeForeground foreground = new();
        AppEntry app = List("Hollow");
        OrchestrationRuntime runtime = Compose(foreground, [effect]);
        runtime.Stop();

        foreground.Raise(app, StartChild().Id);

        Assert.Empty(effect.Journal);
        Assert.Empty(runtime.Sessions);
    }

    [Fact]
    public void ARuleThatFailsLeavesNothingAppliedAndTheAppIsStillFollowed()
    {
        var first = new RecordingEffect("first");
        var failing = new RecordingEffect("failing") { ApplyThrows = true };
        FakeForeground foreground = new();
        AppEntry app = List("Hollow");
        OrchestrationRuntime runtime = Compose(foreground, [first, failing]);

        foreground.Raise(app, StartChild().Id);

        // What the first effect changed is given back at once...
        Assert.Equal(["apply first", "restore first"], first.Journal);
        // ...the app is still a running listed app...
        Assert.Single(runtime.Sessions);

        // ...and Deckle's stop does not undo it a second time.
        runtime.Stop();
        Assert.Equal(["apply first", "restore first"], first.Journal);
    }

    [Fact]
    public void AnAppThatIsDisabledOrNotListedGetsNoRule()
    {
        var effect = new RecordingEffect("hdr");
        FakeForeground foreground = new();
        AppEntry disabled = List("Hollow", enabled: false);
        AppEntry unlisted = new() { DisplayName = "Stranger", ProcessName = UniqueName("Stranger") };
        OrchestrationRuntime runtime = Compose(foreground, [effect]);

        foreground.Raise(disabled, StartChild().Id);
        foreground.Raise(unlisted, StartChild().Id);

        Assert.Empty(effect.Journal);
        Assert.Empty(runtime.Sessions);
    }

    private AppEntry List(string name, bool enabled = true)
    {
        var entry = new AppEntry { DisplayName = name, ProcessName = UniqueName(name), Enabled = enabled };
        Assert.True(_settings.AddEntry(entry));
        _listed.Add(entry);
        return entry;
    }

    // A name no real process carries, so the one-shot enumeration of running
    // processes on each reconcile cannot match it by accident.
    private static string UniqueName(string name) => $"{name}-{Guid.NewGuid():N}";

    private OrchestrationRuntime Compose(FakeForeground foreground, IReadOnlyList<IRuleEffect> effects)
    {
        var runtime = new OrchestrationRuntime(
            _settings, foreground, new FakeProcessStarts(), new ProcessExitWatch(), effects);
        _runtimes.Add(runtime);
        runtime.Start();
        return runtime;
    }

    // A process that lives until the test kills it, with an id the exit watch
    // can really wait on.
    private Process StartChild()
    {
        Process child = Process.Start(new ProcessStartInfo("ping.exe", "-n 300 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        _children.Add(child);
        return child;
    }

    private sealed class FakeForeground : IForegroundMonitor
    {
        public event Action<ForegroundChange>? ForegroundChanged;

        public ForegroundChange? Current { get; private set; }

        public void Raise(AppEntry app, int pid)
        {
            Current = new ForegroundChange(0, pid, app.ProcessName, null, false, null);
            ForegroundChanged?.Invoke(Current);
        }
    }

    private sealed class FakeProcessStarts : IProcessStartSource
    {
        public event Action<ProcessStarted>? Started
        {
            add { }
            remove { }
        }

        public bool Start() => false;

        public void Stop() { }

        public void Dispose() { }
    }

    private sealed class RecordingEffect(string name) : IRuleEffect
    {
        private readonly object _gate = new();
        private readonly List<string> _journal = [];
        private readonly ManualResetEventSlim _restored = new();

        public bool ApplyThrows { get; init; }

        public string Name => name;

        public IReadOnlyList<string> Journal
        {
            get { lock (_gate) return [.. _journal]; }
        }

        public int Applied => Journal.Count(line => line.StartsWith("apply", StringComparison.Ordinal));

        public int Restored => Journal.Count(line => line.StartsWith("restore", StringComparison.Ordinal));

        public bool WaitForRestore(TimeSpan timeout) => _restored.Wait(timeout);

        public bool AppliesTo(AppEntry entry) => true;

        public Action? Apply(AppEntry entry)
        {
            lock (_gate) _journal.Add($"apply {name}");
            if (ApplyThrows) throw new InvalidOperationException($"{name} failed");
            return () =>
            {
                lock (_gate) _journal.Add($"restore {name}");
                _restored.Set();
            };
        }
    }
}
