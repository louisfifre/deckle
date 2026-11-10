using System.Diagnostics;

namespace Deckle.Shell.Taskbar;

// Keeps the native engine in line with the settings. It is the one piece the App
// talks to: it reads TaskbarSettings, maps them onto a TaskbarCustomization
// (TaskbarCustomization.From), and makes it so.
//
//   - The explorer engine runs only while at least one explorer part is on
//     (background, compact tray, compact clock); the Start menu engine only
//     while its toggle is on. Each is a TaskbarEngineHost, created when its
//     first part turns on and stopped (restore, unadvise, unload) when its last
//     part turns off.
//   - A settings change sends the new document to the engines that are
//     connected, so it applies within the settings store's save delay.
//   - An engine that connects, the first time or after Explorer restarted or
//     the Start menu host was relaunched, is sent the document again: the DLL
//     keeps nothing across a restart.
//   - What the engine reports back becomes Problems, the few lines the page
//     shows when a part is not doing what was asked.
//
// One turn at a time (a semaphore): every turn reads the settings as they are
// when it runs, so turns queued behind each other end on the latest ones
// whatever their order. Engine messages arrive on the pipe's thread and never
// wait for a turn; they note what they heard and ask for one.
//
// Nothing here runs in the self-check instance: the App composes it only in a
// resident run (see App.Taskbar.cs).
public sealed class TaskbarCustomizer : IAsyncDisposable
{
    private static readonly Lazy<TaskbarCustomizer> _instance = new(() => new TaskbarCustomizer());
    public static TaskbarCustomizer Instance => _instance.Value;

    private readonly Func<TaskbarSettings> _settings;
    private readonly Func<bool> _isBarVertical;
    private readonly Func<TaskbarTarget, ITaskbarEngine> _createEngine;
    private readonly Func<string, string, Task<FunctionLookup>> _findFunction;
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly object _stateGate = new();
    private readonly TargetState _explorer = new(TaskbarTarget.Explorer);
    private readonly TargetState _startMenu = new(TaskbarTarget.StartMenu);

    private TaskbarCustomization _wanted = new();
    private IReadOnlyDictionary<TaskbarPart, TaskbarProblem> _problems = new Dictionary<TaskbarPart, TaskbarProblem>();
    // The thickness has no status line: a failed lookup, or the engine's
    // constant_return_* record about it, is the problem. Under the state gate.
    private TaskbarProblem? _thicknessLookupProblem;
    private TaskbarProblem? _thicknessEngineProblem;
    private string _loggedParts = "none";
    private bool _loggedCompactTrayLeftOut;
    private bool _loggedCompactTaskButtonsLeftOut;
    private bool _stopped;

    private TaskbarCustomizer()
        : this(() => TaskbarSettingsService.Instance.Current, TaskbarOrientation.IsVertical, CreateHost)
    {
    }

    internal TaskbarCustomizer(
        Func<TaskbarSettings> settings, Func<bool> isBarVertical, Func<TaskbarTarget, ITaskbarEngine> createEngine,
        Func<string, string, Task<FunctionLookup>>? findFunction = null)
    {
        _settings = settings;
        _isBarVertical = isBarVertical;
        _createEngine = createEngine;
        _findFunction = findFunction ?? ((module, symbol) => ExplorerFunctionLocator.Instance.FindAsync(module, symbol));
    }

    /// <summary>
    /// What is wrong with the parts that are on, by part; empty when all is
    /// well. The page reads it on each <see cref="ProblemsChanged"/>.
    /// </summary>
    public IReadOnlyDictionary<TaskbarPart, TaskbarProblem> Problems
    {
        get { lock (_stateGate) return _problems; }
    }

    /// <summary>Raised when <see cref="Problems"/> changes, on whichever thread learned of it.</summary>
    public event Action? ProblemsChanged;

    /// <summary>
    /// Brings the engines in line with the settings as they are now. Returns at
    /// once; the work runs on the thread pool. Idempotent: a call that finds
    /// nothing changed costs a read of the settings.
    /// </summary>
    /// <param name="trigger">startup or setting, as the log spells it.</param>
    public void Reconcile(string trigger) => _ = Task.Run(() => ReconcileAsync(trigger));

    internal async Task ReconcileAsync(string trigger)
    {
        await _turn.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopped) return;

            bool vertical = _isBarVertical();
            TaskbarSettings settings = _settings();
            TaskbarCustomization wanted = TaskbarCustomization.From(settings, vertical);
            lock (_stateGate) _wanted = wanted;
            LogWanted(settings, wanted);

            ExplorerFunction? frameSize = await FindFrameSizeAsync(wanted).ConfigureAwait(false);
            await ApplyAsync(_explorer, wanted.CustomizesExplorer, () => TaskbarDocumentBuilder.Build(wanted, frameSize)).ConfigureAwait(false);
            await ApplyAsync(_startMenu, wanted.CustomizesStartMenu, () => TaskbarDocumentBuilder.BuildStartMenu(wanted)).ConfigureAwait(false);
            RecomputeProblems();
        }
        catch (Exception exception)
        {
            DeckleShellTaskbarSource.Log.TaskbarCustomizerFailed(trigger, exception.GetType().Name);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Restores the taskbar and the Start menu and unloads the engines, and
    /// takes no further settings change. Quitting Deckle does this.
    /// </summary>
    public async Task ShutdownAsync()
    {
        await _turn.WaitAsync().ConfigureAwait(false);
        try
        {
            _stopped = true;
            await Task.WhenAll(StopAsync(_explorer), StopAsync(_startMenu)).ConfigureAwait(false);
            lock (_stateGate) _wanted = new TaskbarCustomization();
            RecomputeProblems();
        }
        finally
        {
            _turn.Release();
        }
    }

    public ValueTask DisposeAsync() => new(ShutdownAsync());

    // Where the thickness function lives in this explorer: a read of the cache,
    // or once per build of Windows a download. A function that this build's
    // symbols do not name is Windows having moved on (not applied); anything
    // else (offline, explorer starting) is an error the next turn tries again.
    private async Task<ExplorerFunction?> FindFrameSizeAsync(TaskbarCustomization wanted)
    {
        if (wanted.BarThickness is null)
        {
            lock (_stateGate) _thicknessLookupProblem = null;
            return null;
        }

        FunctionLookup lookup = await _findFunction(TaskbarDocumentBuilder.FrameSizeModule, TaskbarDocumentBuilder.FrameSizeSymbol).ConfigureAwait(false);
        lock (_stateGate)
        {
            _thicknessLookupProblem = lookup.Function is not null ? null
                : lookup.FunctionGone ? TaskbarProblem.NotApplied
                : TaskbarProblem.Error;
        }
        return lookup.Function;
    }

    // ── One process ───────────────────────────────────────────────────────────

    private async Task ApplyAsync(TargetState process, bool wanted, Func<string> buildDocument)
    {
        if (!wanted)
        {
            await StopAsync(process).ConfigureAwait(false);
            return;
        }

        if (process.Engine is null && !TryStart(process)) return;
        ITaskbarEngine engine = process.Engine!;

        // Explorer's injection happens once at start and again on TaskbarCreated;
        // a person's toggle is a good moment to try again after a failure.
        if (!engine.IsConnected && process.Unavailable && process.Target.Kind == TaskbarTargetKind.Explorer)
            process.Unavailable = !engine.Inject("retry").Succeeded;

        if (!engine.IsConnected) return;   // the document goes out when it says hello

        string document = buildDocument();
        if (document == process.SentDocument) return;

        lock (_stateGate) process.Status = null;   // the old status is about the old document
        int connection = Volatile.Read(ref process.Connection);
        bool sent = await engine.SendAsync(NativeCommand.Apply(document)).ConfigureAwait(false);
        // A DLL that said hello while the write was out has not got this document.
        if (sent && connection == Volatile.Read(ref process.Connection)) process.SentDocument = document;
    }

    private bool TryStart(TargetState process)
    {
        ITaskbarEngine? created = null;
        try
        {
            ITaskbarEngine engine = created = _createEngine(process.Target);
            engine.MessageReceived += message => OnMessage(process, engine, message);
            engine.Disconnected += () => OnDisconnected(process, engine);
            process.Engine = engine;
            process.SentDocument = null;

            InjectionResult first = engine.Start();
            // Explorer has no one else to inject it: a failure here is what the
            // person sees. The Start menu host injects as soon as it is running.
            process.Unavailable = !first.Succeeded && process.Target.Kind == TaskbarTargetKind.Explorer;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The DLL is not next to the app, or the copy could not be made.
            DeckleShellTaskbarSource.Log.NativeEngineUnavailable(
                $"{process.Target.Kind}:{exception.GetType().Name}", exception.HResult);
            process.Engine = null;
            process.Unavailable = true;
            if (created is not null) _ = created.DisposeAsync().AsTask();
            return false;
        }
    }

    private async Task StopAsync(TargetState process)
    {
        ITaskbarEngine? engine = process.Engine;
        process.Engine = null;
        process.SentDocument = null;
        process.Unavailable = false;
        lock (_stateGate) process.Status = null;
        if (engine is null) return;

        bool connected = engine.IsConnected;
        bool unloaded = await engine.StopEngineAsync().ConfigureAwait(false);
        if (connected && !unloaded)
            DeckleShellTaskbarSource.Log.TaskbarEngineUnloadUnconfirmed(process.Target.Kind == TaskbarTargetKind.Explorer ? "explorer" : "start_menu");
        await engine.DisposeAsync().ConfigureAwait(false);
    }

    // ── Engine messages (the pipe's thread) ───────────────────────────────────

    private void OnMessage(TargetState process, ITaskbarEngine engine, NativeMessage message)
    {
        if (!ReferenceEquals(process.Engine, engine)) return;   // a host already let go

        switch (message)
        {
            case NativeHello:
                // A new DLL knows nothing: it gets the document again. The bar
                // may also have changed edge with the restart that brought it.
                Interlocked.Increment(ref process.Connection);
                lock (_stateGate)
                {
                    process.Status = null;
                    if (process == _explorer) _thicknessEngineProblem = null;
                }
                process.Unavailable = false;
                process.SentDocument = null;
                _ = Task.Run(() => ReconcileAsync("engine_connected"));
                break;
            case NativeStatus status:
                lock (_stateGate) process.Status = status;
                RecomputeProblems();
                break;
            case NativeLogRecord record when record.Fields.TryGetValue("id", out string? id) && id == TaskbarDocumentBuilder.BarThicknessId:
                TaskbarProblem? problem = record.Event switch
                {
                    "constant_return_applied" or "constant_return_restored" => null,
                    // Another build than the one resolved, or code that is not
                    // what the file holds (another tool patched it first).
                    "constant_return_skipped" => TaskbarProblem.NotApplied,
                    _ when record.Event.StartsWith("constant_return_", StringComparison.Ordinal) => TaskbarProblem.Error,
                    _ => _thicknessEngineProblem,
                };
                lock (_stateGate) _thicknessEngineProblem = problem;
                RecomputeProblems();
                break;
        }
    }

    private void OnDisconnected(TargetState process, ITaskbarEngine engine)
    {
        if (!ReferenceEquals(process.Engine, engine)) return;
        process.SentDocument = null;
        lock (_stateGate) process.Status = null;
        RecomputeProblems();
    }

    // ── Problems and the log ──────────────────────────────────────────────────

    private void RecomputeProblems()
    {
        IReadOnlyDictionary<TaskbarPart, TaskbarProblem> previous;
        IReadOnlyDictionary<TaskbarPart, TaskbarProblem> next;
        lock (_stateGate)
        {
            next = TaskbarProblemReader.Read(_wanted,
                _explorer.Status, _explorer.Unavailable, _startMenu.Status, _startMenu.Unavailable,
                _thicknessLookupProblem ?? _thicknessEngineProblem);
            previous = _problems;
            if (SameProblems(previous, next)) return;
            _problems = next;
        }

        foreach ((TaskbarPart part, TaskbarProblem problem) in next)
        {
            if (!previous.TryGetValue(part, out TaskbarProblem known) || known != problem)
                DeckleShellTaskbarSource.Log.TaskbarPartNotInEffect(part.ForLog(), ProblemName(problem));
        }
        foreach (TaskbarPart part in previous.Keys)
        {
            if (!next.ContainsKey(part)) DeckleShellTaskbarSource.Log.TaskbarPartProblemEnded(part.ForLog());
        }

        ProblemsChanged?.Invoke();
    }

    private static bool SameProblems(
        IReadOnlyDictionary<TaskbarPart, TaskbarProblem> a, IReadOnlyDictionary<TaskbarPart, TaskbarProblem> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out TaskbarProblem other) && other == pair.Value);

    private static string ProblemName(TaskbarProblem problem) => problem switch
    {
        TaskbarProblem.NotApplied => "not_applied",
        TaskbarProblem.Contested => "contested",
        _ => "error",
    };

    // One line when the set of parts changes, one when a part is on but has
    // nothing to ask (the compact tray on a horizontal bar). Turns happen one
    // at a time, so these fields need no lock.
    private void LogWanted(TaskbarSettings settings, TaskbarCustomization wanted)
    {
        string parts = string.Join(',', TaskbarProblemReader.WantedParts(wanted).Select(part => part.ForLog()));
        if (parts.Length == 0) parts = "none";
        if (parts != _loggedParts)
        {
            _loggedParts = parts;
            DeckleShellTaskbarSource.Log.TaskbarPartsChanged(parts);
        }

        bool leftOut = settings.CompactTray && wanted.TrayIconPitch is null;
        if (leftOut != _loggedCompactTrayLeftOut)
        {
            _loggedCompactTrayLeftOut = leftOut;
            if (leftOut)
                DeckleShellTaskbarSource.Log.TaskbarPartSkipped(TaskbarPart.CompactTray.ForLog(), "taskbar_horizontal");
        }

        bool buttonsLeftOut = settings.CompactTaskButtons && wanted.TaskButtonPitch is null;
        if (buttonsLeftOut != _loggedCompactTaskButtonsLeftOut)
        {
            _loggedCompactTaskButtonsLeftOut = buttonsLeftOut;
            if (buttonsLeftOut)
                DeckleShellTaskbarSource.Log.TaskbarPartSkipped(TaskbarPart.CompactTaskButtons.ForLog(), "taskbar_horizontal");
        }
    }

    private static ITaskbarEngine CreateHost(TaskbarTarget target)
    {
        string source = Path.Combine(AppContext.BaseDirectory, VersionedNativeCopy.DllName);
        if (!File.Exists(source)) throw new FileNotFoundException("The taskbar engine is not next to the app.", source);
        return new TaskbarEngineHost(source, new VersionedNativeCopy(VersionedNativeCopy.DefaultRoot()), target);
    }

    // What the customizer knows about one target process. Engine, SentDocument
    // and Unavailable change only inside a turn (and, for the last two, when
    // the engine says hello, which is followed by a turn); Status is the engine's
    // latest report and is guarded by the state gate.
    private sealed class TargetState(TaskbarTarget target)
    {
        public TaskbarTarget Target { get; } = target;
        public volatile ITaskbarEngine? Engine;
        public volatile string? SentDocument;
        public volatile bool Unavailable;
        public int Connection;   // counts the DLLs that said hello
        public NativeStatus? Status;
    }
}
