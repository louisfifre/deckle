using System.Text.Json;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The customizer against engines that are not in any process: what it starts,
// what it sends, and when it lets go. The fake stands where TaskbarEngineHost
// does, and "connects" the way the DLL does, by saying hello.
[Trait("Category", "unit")]
public sealed class TaskbarCustomizerTests
{
    private sealed class FakeEngine(TaskbarTarget target) : ITaskbarEngine
    {
        public TaskbarTarget Target { get; } = target;
        public bool IsConnected { get; private set; }
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public bool InjectionSucceeds { get; set; } = true;
        public bool HeldOff { get; set; }
        public List<string> Sent { get; } = [];

        public event Action<NativeMessage>? MessageReceived;
        public event Action? Disconnected;

        public InjectionResult Start()
        {
            Started = true;
            return InjectionSucceeds ? InjectionResult.Success : InjectionResult.Failed("find_taskbar_window", 1400);
        }

        public InjectionResult Inject(string trigger) =>
            InjectionSucceeds ? InjectionResult.Success : InjectionResult.Failed("find_taskbar_window", 1400);

        public Task<bool> SendAsync(string json, CancellationToken cancellation = default)
        {
            if (!IsConnected) return Task.FromResult(false);
            Sent.Add(json);
            return Task.FromResult(true);
        }

        public Task<bool> StopEngineAsync()
        {
            Stopped = true;
            IsConnected = false;
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void SayHello()
        {
            IsConnected = true;
            MessageReceived?.Invoke(new NativeHello(Protocol: 1, ProcessId: 4242, ModulePath: "native.dll"));
        }

        public void Report(params NativeRuleState[] rules) =>
            MessageReceived?.Invoke(new NativeStatus(1, true, rules, [], []));

        public void Log(string name, string id) =>
            MessageReceived?.Invoke(new NativeLogRecord(NativeLogLevel.Info, name, "", new Dictionary<string, string> { ["id"] = id }));

        public void Leave()
        {
            IsConnected = false;
            Disconnected?.Invoke();
        }
    }

    private readonly TaskbarSettings _settings = new();
    private readonly List<FakeEngine> _engines = [];
    private bool _vertical = true;
    private bool _dllMissing;

    // Where the thickness function lives; never the network in a test.
    private FunctionLookup _frameSize = new(new ExplorerFunction(
        "Taskbar.View.dll", 0x6AA3487A, 10055680, 0x451F0, new byte[16]), null);

    private TaskbarCustomizer Customizer() => new(() => _settings, () => _vertical, target =>
    {
        if (_dllMissing) throw new FileNotFoundException("no dll");
        var engine = new FakeEngine(target);
        _engines.Add(engine);
        return engine;
    }, (_, _) => Task.FromResult(_frameSize));

    private static bool HasConstantReturn(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.GetProperty("doc").TryGetProperty("constantReturns", out _);
    }

    private FakeEngine? Engine(TaskbarTargetKind kind) => _engines.LastOrDefault(engine => engine.Target.Kind == kind);

    private static NativeRuleState Rule(string id, string state) => new(id, state, 1, 1, 0, 0, "");

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "the condition did not come true in time");
    }

    private static bool HasRule(string line, string id)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.GetProperty("doc").GetProperty("rules").EnumerateArray()
            .Any(rule => rule.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task TheThicknessGoesOutWithTheFunctionTheLookupFound()
    {
        _settings.CustomBarThickness = true;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");

        FakeEngine explorer = Engine(TaskbarTargetKind.Explorer)!;
        explorer.SayHello();

        await WaitUntil(() => explorer.Sent.Count == 1);
        Assert.True(HasConstantReturn(explorer.Sent[0]));
        Assert.Empty(customizer.Problems);
    }

    // A host that gave up injecting (its target kept dying just after it
    // received the engine) leaves the parts unapplied until Deckle restarts:
    // the page must say so, where an ordinary loss says nothing and waits for
    // the engine to come back.
    [Fact]
    public async Task AnEngineHeldOffAfterRepeatedLossesPutsItsPartsInError()
    {
        _settings.Background = TaskbarBackground.Transparent;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        FakeEngine explorer = Engine(TaskbarTargetKind.Explorer)!;
        explorer.SayHello();
        await WaitUntil(() => explorer.Sent.Count == 1);

        explorer.Leave();
        Assert.Empty(customizer.Problems);

        explorer.SayHello();
        await WaitUntil(() => explorer.Sent.Count == 2);
        explorer.HeldOff = true;
        explorer.Leave();

        Assert.Equal(TaskbarProblem.Error, customizer.Problems[TaskbarPart.Background]);
    }

    [Fact]
    public async Task AFunctionThisBuildNoLongerNamesLeavesTheThicknessNotApplied()
    {
        _settings.CustomBarThickness = true;
        _frameSize = new(null, FunctionLookup.SymbolMissing);
        TaskbarCustomizer customizer = Customizer();

        await customizer.ReconcileAsync("startup");

        Assert.Equal(TaskbarProblem.NotApplied, customizer.Problems[TaskbarPart.BarThickness]);
    }

    [Fact]
    public async Task ALookupThatCouldNotReachTheServerIsAnError()
    {
        _settings.CustomBarThickness = true;
        _frameSize = new(null, "download_failed");
        TaskbarCustomizer customizer = Customizer();

        await customizer.ReconcileAsync("startup");

        Assert.Equal(TaskbarProblem.Error, customizer.Problems[TaskbarPart.BarThickness]);
    }

    [Fact]
    public async Task TheEngineLeavingTheFunctionAloneIsSaidUntilItAppliesIt()
    {
        _settings.CustomBarThickness = true;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        FakeEngine explorer = Engine(TaskbarTargetKind.Explorer)!;
        explorer.SayHello();
        await WaitUntil(() => explorer.Sent.Count == 1);

        explorer.Log("constant_return_skipped", TaskbarDocumentBuilder.BarThicknessId);
        Assert.Equal(TaskbarProblem.NotApplied, customizer.Problems[TaskbarPart.BarThickness]);

        explorer.Log("constant_return_applied", TaskbarDocumentBuilder.BarThicknessId);
        Assert.Empty(customizer.Problems);
    }

    [Fact]
    public async Task WhenNothingIsOnNoEngineIsStarted()
    {
        TaskbarCustomizer customizer = Customizer();

        await customizer.ReconcileAsync("startup");

        Assert.Empty(_engines);
        Assert.Empty(customizer.Problems);
    }

    [Fact]
    public async Task AnExplorerPartStartsTheExplorerEngineAndNotTheStartMenuOne()
    {
        _settings.Background = TaskbarBackground.Transparent;
        TaskbarCustomizer customizer = Customizer();

        await customizer.ReconcileAsync("startup");

        FakeEngine explorer = Assert.Single(_engines);
        Assert.Equal(TaskbarTargetKind.Explorer, explorer.Target.Kind);
        Assert.True(explorer.Started);
    }

    [Fact]
    public async Task TheStartMenuToggleStartsTheStartMenuEngineAlone()
    {
        _settings.StartMenuFullHeight = true;
        TaskbarCustomizer customizer = Customizer();

        await customizer.ReconcileAsync("startup");

        FakeEngine startMenu = Assert.Single(_engines);
        Assert.Equal(TaskbarTargetKind.StartMenu, startMenu.Target.Kind);
    }

    // The DLL knows nothing until it says hello, and the document goes out then.
    [Fact]
    public async Task TheDocumentIsSentWhenTheEngineConnects()
    {
        _settings.Background = TaskbarBackground.Black;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        FakeEngine explorer = Engine(TaskbarTargetKind.Explorer)!;
        Assert.Empty(explorer.Sent);

        explorer.SayHello();

        await WaitUntil(() => explorer.Sent.Count == 1);
        Assert.True(HasRule(explorer.Sent[0], "background.fill"));
    }

    [Fact]
    public async Task ASettingsChangeSendsTheNewDocumentAndAnUnchangedOneSendsNothing()
    {
        _settings.Background = TaskbarBackground.Transparent;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        FakeEngine explorer = Engine(TaskbarTargetKind.Explorer)!;
        explorer.SayHello();
        await WaitUntil(() => explorer.Sent.Count == 1);

        await customizer.ReconcileAsync("setting");
        Assert.Single(explorer.Sent);

        _settings.CompactClock = true;
        await customizer.ReconcileAsync("setting");

        Assert.Equal(2, explorer.Sent.Count);
        Assert.Contains("\"clock\"", explorer.Sent[1]);
    }

    // A restarted Explorer builds a new DLL that has none of it.
    [Fact]
    public async Task AnEngineThatConnectsAgainIsSentTheDocumentAgain()
    {
        _settings.CompactClock = true;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        FakeEngine explorer = Engine(TaskbarTargetKind.Explorer)!;
        explorer.SayHello();
        await WaitUntil(() => explorer.Sent.Count == 1);

        explorer.Leave();
        explorer.SayHello();

        await WaitUntil(() => explorer.Sent.Count == 2);
    }

    [Fact]
    public async Task TurningTheLastExplorerPartOffRestoresAndUnloadsTheEngine()
    {
        _settings.Background = TaskbarBackground.Transparent;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        FakeEngine first = Engine(TaskbarTargetKind.Explorer)!;
        first.SayHello();
        await WaitUntil(() => first.Sent.Count == 1);

        _settings.Background = TaskbarBackground.Native;
        await customizer.ReconcileAsync("setting");

        Assert.True(first.Stopped);
        Assert.True(first.Disposed);

        _settings.Background = TaskbarBackground.Black;
        await customizer.ReconcileAsync("setting");

        FakeEngine second = Engine(TaskbarTargetKind.Explorer)!;
        Assert.NotSame(first, second);
        Assert.True(second.Started);
    }

    // The engine for explorer stays while any explorer part is on, and the
    // document just loses the part that went off.
    [Fact]
    public async Task TurningOnePartOffKeepsTheEngineForTheOthers()
    {
        _settings.Background = TaskbarBackground.Transparent;
        _settings.CompactClock = true;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        FakeEngine explorer = Engine(TaskbarTargetKind.Explorer)!;
        explorer.SayHello();
        await WaitUntil(() => explorer.Sent.Count == 1);

        _settings.CompactClock = false;
        await customizer.ReconcileAsync("setting");

        Assert.False(explorer.Stopped);
        Assert.Equal(2, explorer.Sent.Count);
        Assert.DoesNotContain("\"clock\"", explorer.Sent[1]);
        Assert.True(HasRule(explorer.Sent[1], "background.fill"));
    }

    [Fact]
    public async Task TheStartMenuEngineFollowsItsOwnToggle()
    {
        _settings.Background = TaskbarBackground.Transparent;
        _settings.StartMenuFullHeight = true;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        Assert.Equal(2, _engines.Count);

        _settings.StartMenuFullHeight = false;
        await customizer.ReconcileAsync("setting");

        Assert.True(Engine(TaskbarTargetKind.StartMenu)!.Stopped);
        Assert.False(Engine(TaskbarTargetKind.Explorer)!.Stopped);
    }

    [Fact]
    public async Task TheCompactTrayWaitsForAVerticalBar()
    {
        _settings.CompactTray = true;
        _vertical = false;
        TaskbarCustomizer customizer = Customizer();

        await customizer.ReconcileAsync("startup");
        Assert.Empty(_engines);

        _vertical = true;
        await customizer.ReconcileAsync("setting");
        Assert.Single(_engines);
    }

    // The bar may have changed edge with the restart that brought a new DLL.
    [Fact]
    public async Task TheBarIsAskedAgainWhenTheEngineConnects()
    {
        _settings.CompactTray = true;
        _settings.CompactClock = true;
        _vertical = false;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        FakeEngine explorer = Engine(TaskbarTargetKind.Explorer)!;
        explorer.SayHello();
        await WaitUntil(() => explorer.Sent.Count == 1);
        Assert.False(HasRule(explorer.Sent[0], "tray.icon.minHeight"));

        _vertical = true;
        explorer.SayHello();

        await WaitUntil(() => explorer.Sent.Count == 2);
        Assert.True(HasRule(explorer.Sent[1], "tray.icon.minHeight"));
    }

    [Fact]
    public async Task AMissingEngineIsAnErrorOnEveryPartThatWasAskedFor()
    {
        _dllMissing = true;
        _settings.Background = TaskbarBackground.Black;
        _settings.StartMenuFullHeight = true;
        TaskbarCustomizer customizer = Customizer();
        int raised = 0;
        customizer.ProblemsChanged += () => raised++;

        await customizer.ReconcileAsync("startup");

        Assert.Equal(TaskbarProblem.Error, customizer.Problems[TaskbarPart.Background]);
        Assert.Equal(TaskbarProblem.Error, customizer.Problems[TaskbarPart.StartMenuHeight]);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task APartTheEngineCouldNotFindIsToldAndThenForgottenWhenItIsApplied()
    {
        _settings.CompactClock = true;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");
        FakeEngine explorer = Engine(TaskbarTargetKind.Explorer)!;
        explorer.SayHello();
        await WaitUntil(() => explorer.Sent.Count == 1);

        explorer.Report(Rule("clock.spacing", "applied"), Rule("clock.upper.fontSize", "never_matched"));
        Assert.Equal(TaskbarProblem.NotApplied, customizer.Problems[TaskbarPart.CompactClock]);

        explorer.Report(Rule("clock.spacing", "applied"), Rule("clock.upper.fontSize", "applied"));
        Assert.Empty(customizer.Problems);
    }

    [Fact]
    public async Task QuittingRestoresAndUnloadsBothEngines()
    {
        _settings.Background = TaskbarBackground.Black;
        _settings.StartMenuFullHeight = true;
        TaskbarCustomizer customizer = Customizer();
        await customizer.ReconcileAsync("startup");

        await customizer.ShutdownAsync();

        Assert.All(_engines, engine => Assert.True(engine.Stopped && engine.Disposed));

        _settings.CompactClock = true;
        await customizer.ReconcileAsync("setting");
        Assert.Equal(2, _engines.Count);   // nothing starts after the shutdown
    }
}
