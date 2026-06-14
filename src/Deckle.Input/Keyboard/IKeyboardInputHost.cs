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

    /// <summary>Raised on the input thread when the foreground window or focused element changes.</summary>
    event Action? FocusChanged;

    /// <summary>Spawns the host and begins observation; false (and logged) when native setup failed.</summary>
    bool Start();

    /// <summary>Stops observation and unwinds the host.</summary>
    void Stop();
}
