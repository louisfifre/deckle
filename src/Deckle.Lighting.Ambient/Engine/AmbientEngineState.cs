namespace Deckle.Lighting.Ambient;

// State machine for AmbientEngine, exposed via the StateChanged event.
// Five values let consumers distinguish a transient "in flight" state from
// a settled Off / Running, and surface failures explicitly.
//
// Canonical transitions :
//   Off  → Starting  → Running         (successful StartAsync)
//   Off  → Starting  → Error → Off     (StartAsync threw or refused)
//   Running → Stopping → Off            (Stop)
//
// Error is a transient signal — the engine collapses back to Off
// immediately after raising it so consumers don't sit in a sticky
// failure state ; the actual error message is surfaced via the
// DeckleAmbientSource (Error level) and through the App-side Enabled revert.
public enum AmbientEngineState
{
    /// <summary>Engine idle — no capture, no Hue traffic, no owned deps.</summary>
    Off,

    /// <summary>Engine is constructing its dependencies (capture init,
    /// Hue bridge connect, ListLights).</summary>
    Starting,

    Running,

    /// <summary>Engine is tearing down : the loop is cancelled and the
    /// owned deps are released on the thread pool ; Off follows once
    /// they are.</summary>
    Stopping,

    /// <summary>Transient — the last StartAsync failed (pair invalid,
    /// network down, bridge unreachable). Off is raised right after it ;
    /// consumers should display the error briefly then return to the
    /// Off rendering.</summary>
    Error,
}
