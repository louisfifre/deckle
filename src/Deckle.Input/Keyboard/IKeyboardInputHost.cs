namespace Deckle.Input;

// The keyboard observation host as its consumers depend on it: three
// input-thread signals plus a start/stop lifecycle. KeyboardInputHost is the
// production implementation, owning a dedicated Raw Input thread. The interface
// is the module's port: it lets a consumer — the autocorrect engine, and later
// the app host — be driven from a substitute that raises these signals
// directly, with no native pump.
public interface IKeyboardInputHost
{
    /// <summary>Raised on the input thread for every non-overrun keyboard transition.</summary>
    event Action<KeyboardKeyEvent>? KeyReceived;

    /// <summary>Raised on the input thread when any mouse button transitions to down.</summary>
    event Action? PointerInteraction;

    /// <summary>Raised on the input thread for every mouse-wheel transition (vertical or horizontal).</summary>
    event Action<MouseWheelEvent>? WheelObserved;

    /// <summary>Raised on the input thread when the foreground window or focused element changes.</summary>
    event Action? FocusChanged;

    /// <summary>
    /// Raised on the input thread after <see cref="RequestDrain"/> posted to the pump. The
    /// marshalling point for a background consumer (the autocorrect reranker) to apply a result
    /// back on the input thread, where the engine's state lives.
    /// </summary>
    event Action? DrainRequested;

    /// <summary>
    /// Posts a drain request to the input thread; safe to call from any thread (a bare thread
    /// message). The pump raises <see cref="DrainRequested"/> on the input thread when it arrives.
    /// A no-op before the pump exists or after it has quit.
    /// </summary>
    void RequestDrain();

    /// <summary>Spawns the host and begins observation; false (and logged) when native setup failed.</summary>
    bool Start();

    /// <summary>Stops observation and unwinds the host.</summary>
    void Stop();
}
