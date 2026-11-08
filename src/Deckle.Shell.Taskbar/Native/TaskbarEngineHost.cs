namespace Deckle.Shell.Taskbar;

// Runs the native engine from Deckle's side: keeps the versioned copy of the
// DLL, serves the pipe, injects the DLL into explorer, injects again each time
// explorer rebuilds its taskbar, and stops the engine on request.
//
// Nothing in the app creates one yet. The development console
// (Deckle.Shell.Taskbar.Harness) is its first user.
internal sealed class TaskbarEngineHost : IAsyncDisposable
{
    private static readonly TimeSpan _unloadTimeout = TimeSpan.FromSeconds(10);

    private readonly string _sourceDllPath;
    private readonly VersionedNativeCopy _copy;
    private readonly TaskbarPipeServer _server;
    private readonly Func<string, InjectionResult>? _injector;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private TaskbarCreatedListener? _listener;
    private Task? _serverTask;
    private string _loadedPath = "";
    private int _explorerPid;
    private volatile bool _unloadReported;
    private TaskCompletionSource _disconnected = NewSignal();

    /// <param name="sourceDllPath">The DLL next to the app; the host loads its versioned copy.</param>
    /// <param name="copy">Where the versioned copies live.</param>
    /// <param name="server">The pipe server to serve; the host owns it.</param>
    /// <param name="injector">
    /// Puts the DLL (given its path) into another process than the taskbar's
    /// explorer; null means explorer, with a TaskbarCreated listener. A custom
    /// injector is called once, and nothing re-injects it.
    /// </param>
    public TaskbarEngineHost(string sourceDllPath, VersionedNativeCopy copy, TaskbarPipeServer server,
        Func<string, InjectionResult>? injector = null)
    {
        _injector = injector;
        _sourceDllPath = sourceDllPath;
        _copy = copy;
        _server = server;
        _server.MessageReceived += OnMessage;
        _server.ClientConnected += () => _disconnected = NewSignal();
        _server.ClientDisconnected += OnDisconnected;
    }

    /// <summary>Every message from the DLL, after the host has logged it.</summary>
    public event Action<NativeMessage>? MessageReceived;

    /// <summary>The path explorer was asked to load.</summary>
    public string LoadedPath => _loadedPath;

    /// <summary>The explorer process the DLL said hello from; 0 before.</summary>
    public int ExplorerProcessId => _explorerPid;

    /// <summary>Whether the DLL said it was unloading before its pipe closed.</summary>
    public bool UnloadReported => _unloadReported;

    public bool IsConnected => _server.IsConnected;

    /// <summary>
    /// Copies the DLL, sweeps the old copies, starts serving the pipe, listens
    /// for TaskbarCreated and injects once. Returns the injection's outcome.
    /// </summary>
    public InjectionResult Start()
    {
        _loadedPath = _copy.Ensure(_sourceDllPath);
        _copy.Sweep(_loadedPath);

        _serverTask = Task.Run(() => _server.RunAsync(_cancellation.Token));
        if (_injector is null) _listener = new TaskbarCreatedListener(() => Inject("explorer_restart"));
        return Inject("startup");
    }

    /// <summary>Injects the DLL into the current explorer. Safe to call again.</summary>
    public InjectionResult Inject(string trigger)
    {
        lock (_gate)
        {
            InjectionResult result = _injector is null ? TaskbarHookInjector.Inject(_loadedPath) : _injector(_loadedPath);
            if (result.Succeeded)
                DeckleShellTaskbarSource.Log.NativeEngineInjected(trigger);
            else
                DeckleShellTaskbarSource.Log.NativeEngineUnavailable(result.Step, result.Win32Error);
            return result;
        }
    }

    public Task<bool> SendAsync(string json, CancellationToken cancellation = default) =>
        _server.SendAsync(json, cancellation);

    /// <summary>
    /// Asks the DLL to restore, unadvise and unload, and waits for its pipe to
    /// close. True when the DLL reported that it unloaded itself.
    /// </summary>
    public async Task<bool> StopEngineAsync()
    {
        if (!_server.IsConnected) return false;

        Task closed = _disconnected.Task;
        // A failed send still waits: the DLL may have left on its own a moment ago.
        await _server.SendAsync(NativeCommand.Stop).ConfigureAwait(false);
        await Task.WhenAny(closed, Task.Delay(_unloadTimeout)).ConfigureAwait(false);
        return _unloadReported;
    }

    private void OnMessage(NativeMessage message)
    {
        switch (message)
        {
            case NativeHello hello:
                _explorerPid = hello.ProcessId;
                _unloadReported = false;
                DeckleShellTaskbarSource.Log.NativeEngineConnected(hello.ProcessId, hello.Protocol);
                break;
            case NativeLogRecord record:
                if (record.Event == "unloading") _unloadReported = true;
                NativeLogRelay.Relay(record);
                break;
        }
        MessageReceived?.Invoke(message);
    }

    private void OnDisconnected()
    {
        DeckleShellTaskbarSource.Log.NativeEngineDisconnected(_unloadReported ? "unloaded" : "lost");
        _disconnected.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        _listener?.Dispose();
        _cancellation.Cancel();
        if (_serverTask is not null)
        {
            try { await _serverTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await _server.DisposeAsync().ConfigureAwait(false);
        _cancellation.Dispose();
    }
}


