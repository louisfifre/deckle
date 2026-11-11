namespace Deckle.Shell.Taskbar;

// What Deckle asks of the native engine, seen from the host that serves it: the
// part of TaskbarEngineHost the customizer uses, so the customizer can be
// driven in a test without an explorer to inject into.
internal interface ITaskbarEngine : IAsyncDisposable
{
    /// <summary>Whether the injected DLL is connected to the pipe now.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Whether the host gave up injecting: its target died several times in a row
    /// just after receiving the engine (InjectionBreaker). It stays so until
    /// Deckle restarts.
    /// </summary>
    bool HeldOff { get; }

    /// <summary>Every message from the DLL, after the host has logged it.</summary>
    event Action<NativeMessage>? MessageReceived;

    /// <summary>Raised when the DLL's pipe closes, however it came to close.</summary>
    event Action? Disconnected;

    /// <summary>Serves the pipe and injects once; the host then keeps the engine in its target.</summary>
    InjectionResult Start();

    /// <summary>Injects the DLL into the target as it is now.</summary>
    InjectionResult Inject(string trigger);

    /// <summary>Sends one command line; false when no DLL is connected or the write failed.</summary>
    Task<bool> SendAsync(string json, CancellationToken cancellation = default);

    /// <summary>
    /// Asks the DLL to restore everything and end its session. True when it said it stopped:
    /// parked in its target, or unloaded.
    /// </summary>
    Task<bool> StopEngineAsync();
}
