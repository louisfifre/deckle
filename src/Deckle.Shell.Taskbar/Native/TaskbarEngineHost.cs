using System.Diagnostics;

namespace Deckle.Shell.Taskbar;

// Runs the native engine from Deckle's side for one target process: keeps the
// versioned copy of the DLL, serves the target's pipe, injects the DLL, injects
// again when the target is rebuilt, and stops the engine on request. One host
// per target (TaskbarTarget): the taskbar and the Start menu each get their own.
//
// How a target is found again after it was rebuilt:
//   - explorer: Windows broadcasts TaskbarCreated each time it builds its
//     taskbar (TaskbarCreatedListener), which is an event, not a poll.
//   - the Start menu host: Windows kills and relaunches StartMenuExperienceHost
//     now and then, and announces nothing a normal user can listen to (a WMI or
//     ETW process-start trace needs administrator rights). So the host waits on
//     the live process's handle, which costs nothing while it runs, and when the
//     process is gone it looks for the new one every few seconds until it
//     appears. The look is one process-list read; the cadence is the slow part.
//
// Nothing in the app creates one yet. The development console
// (Deckle.Shell.Taskbar.Harness) is its first user.
internal sealed class TaskbarEngineHost : IAsyncDisposable
{
    private static readonly TimeSpan _unloadTimeout = TimeSpan.FromSeconds(10);

    // How often a missing target process is looked for, and the longest wait
    // after a failed injection (it doubles from the first up to the second).
    private static readonly TimeSpan _appearancePoll = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _retryCeiling = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan _perThreadWait = TimeSpan.FromMilliseconds(400);

    private readonly string _sourceDllPath;
    private readonly VersionedNativeCopy _copy;
    private readonly TaskbarPipeServer _server;
    private readonly TaskbarTarget _target;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private TaskbarCreatedListener? _listener;
    private Task? _serverTask;
    private Task? _watchTask;
    private string _loadedPath = "";
    private int _targetPid;
    private volatile bool _stopRequested;
    private volatile bool _unloadReported;
    private TaskCompletionSource _disconnected = NewSignal();

    /// <param name="sourceDllPath">The DLL next to the app; the host loads its versioned copy.</param>
    /// <param name="copy">Where the versioned copies live.</param>
    /// <param name="server">The pipe server to serve; the host owns it.</param>
    /// <param name="target">The process to customize; its pipe and injection route follow from it.</param>
    /// <param name="server">The pipe server to serve; null creates the target's own. The host owns it.</param>
    public TaskbarEngineHost(string sourceDllPath, VersionedNativeCopy copy, TaskbarTarget target,
        TaskbarPipeServer? server = null)
    {
        _target = target;
        _sourceDllPath = sourceDllPath;
        _copy = copy;
        _server = server ?? new TaskbarPipeServer(TaskbarPipeServer.NameFor(target), target.Packaged);
        _server.MessageReceived += OnMessage;
        _server.ClientConnected += () => _disconnected = NewSignal();
        _server.ClientDisconnected += OnDisconnected;
    }

    /// <summary>Every message from the DLL, after the host has logged it.</summary>
    public event Action<NativeMessage>? MessageReceived;

    /// <summary>The path explorer was asked to load.</summary>
    public string LoadedPath => _loadedPath;

    public TaskbarTarget Target => _target;

    /// <summary>The target process the DLL said hello from; 0 before.</summary>
    public int TargetProcessId => _targetPid;

    /// <summary>Whether the DLL said it was unloading before its pipe closed.</summary>
    public bool UnloadReported => _unloadReported;

    public bool IsConnected => _server.IsConnected;

    /// <summary>
    /// Copies the DLL, sweeps the old copies, starts serving the pipe and
    /// injects once; then keeps the engine in the target (TaskbarCreated for
    /// explorer, the process watch for the Start menu). Returns the first
    /// injection's outcome; for the Start menu that is a failure when the host
    /// is not running yet, and the watch injects as soon as it is.
    /// </summary>
    public InjectionResult Start()
    {
        _loadedPath = _copy.Ensure(_sourceDllPath);
        _copy.Sweep(_loadedPath);
        if (_target.Packaged) VersionedNativeCopy.GrantPackagedAccess(_loadedPath);

        _serverTask = Task.Run(() => _server.RunAsync(_cancellation.Token));
        if (_target.Kind == TaskbarTargetKind.Explorer)
        {
            _listener = new TaskbarCreatedListener(() => Inject("explorer_restart"));
            return Inject("startup");
        }

        InjectionResult first = Inject("startup");
        _watchTask = Task.Run(() => WatchProcessAsync(_cancellation.Token));
        return first;
    }

    /// <summary>Injects the DLL into the target as it is now. Safe to call again.</summary>
    public InjectionResult Inject(string trigger)
    {
        lock (_gate)
        {
            InjectionResult result = _target.Kind == TaskbarTargetKind.Explorer
                ? TaskbarHookInjector.Inject(_loadedPath)
                : InjectIntoProcess();
            if (result.Succeeded)
                DeckleShellTaskbarSource.Log.NativeEngineInjected($"{_target.Kind}:{trigger}");
            else
                DeckleShellTaskbarSource.Log.NativeEngineUnavailable($"{_target.Kind}:{result.Step}", result.Win32Error);
            return result;
        }
    }

    private InjectionResult InjectIntoProcess()
    {
        using Process? process = FindTargetProcess();
        if (process is null) return InjectionResult.Failed("find_process", 0);
        return TaskbarHookInjector.InjectIntoThreads(_loadedPath, process.Id, () => _server.IsConnected, _perThreadWait);
    }

    private Process? FindTargetProcess()
    {
        Process? found = null;
        foreach (Process candidate in Process.GetProcessesByName(_target.ProcessName))
        {
            bool usable = found is null && !HasExited(candidate);
            if (usable) found = candidate; else candidate.Dispose();
        }
        return found;
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
    }

    // Keeps the engine in a process that Windows may kill and relaunch: injects
    // when the process is there and the DLL is not connected, then sleeps on the
    // process handle until it exits. Runs until the host is disposed or the
    // engine was stopped on purpose.
    private async Task WatchProcessAsync(CancellationToken cancellation)
    {
        TimeSpan retry = _appearancePoll;
        try
        {
            while (!cancellation.IsCancellationRequested && !_stopRequested)
            {
                using Process? process = FindTargetProcess();
                if (process is null)
                {
                    await Task.Delay(_appearancePoll, cancellation).ConfigureAwait(false);
                    continue;
                }

                if (!_server.IsConnected)
                {
                    InjectionResult result = await Task.Run(() => Inject("process_started"), cancellation).ConfigureAwait(false);
                    if (!result.Succeeded)
                    {
                        // The host may still be starting (no message queue yet); try again, slower each time.
                        await Task.Delay(retry, cancellation).ConfigureAwait(false);
                        retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, _retryCeiling.Ticks));
                        continue;
                    }
                    retry = _appearancePoll;
                }

                try { await process.WaitForExitAsync(cancellation).ConfigureAwait(false); }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    await Task.Delay(_appearancePoll, cancellation).ConfigureAwait(false);   // no handle to wait on: look again
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public Task<bool> SendAsync(string json, CancellationToken cancellation = default) =>
        _server.SendAsync(json, cancellation);

    /// <summary>
    /// Asks the DLL to restore, unadvise and unload, and waits for its pipe to
    /// close. True when the DLL reported that it unloaded itself.
    /// </summary>
    public async Task<bool> StopEngineAsync()
    {
        _stopRequested = true;
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
                _targetPid = hello.ProcessId;
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
        if (_watchTask is not null)
        {
            try { await _watchTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (_serverTask is not null)
        {
            try { await _serverTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        await _server.DisposeAsync().ConfigureAwait(false);
        _cancellation.Dispose();
    }
}


