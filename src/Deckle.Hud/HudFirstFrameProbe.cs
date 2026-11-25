using System.Diagnostics;

namespace Deckle.Hud;

// Holds one shortcut press until the HUD's next frame, so the event is
// written once per state change and carries the time from the press.
internal sealed class HudFirstFrameProbe
{
    private string? _state;
    private long _pressTimestamp;

    public bool IsArmed => _state is not null;

    // A newer state change replaces a pending one: only the state on screen
    // at the frame is reported.
    public void Arm(string state, long pressTimestamp)
    {
        _state = state;
        _pressTimestamp = pressTimestamp;
    }

    public void Cancel() => _state = null;

    // Called on a frame; reports the pending state once, then disarms.
    public void OnFrame(DeckleHudSource log, long nowTimestamp)
    {
        if (_state is null) return;
        string state = _state;
        _state = null;
        int ms = (int)Stopwatch.GetElapsedTime(_pressTimestamp, nowTimestamp).TotalMilliseconds;
        log.HudFirstFramePresented(state, ms);
    }
}
