using Deckle.Composition;

namespace Deckle.Hud;

// The latest capture snapshot can replace an older one without losing a cut.
// Holds a full gauge briefly so a cut remains visible after immediate speech.
//
// Samples stay raw here. The smoothing is the visual's motion, one named
// duration per kind of move (UtteranceCountdownVisual.SetProgress), not a
// filter on the data: a filter would hide the capture's timing behind an
// audio constant, where a motion keeps the gauge in step with the rest of
// the HUD's animations.
//
// The hold decides when the move back to rest starts, not which move it is:
// ValueAt stays at 1 for CompletionDuration after a cut, and the visual
// returns to rest on its one retreat when the value finally reads 0.
internal sealed class UtteranceCountdownState
{
    internal static readonly TimeSpan CompletionDuration = TimeSpan.FromMilliseconds(300);
    private int _completed;
    private float _progress;
    private TimeSpan _completionUntil;

    public void Accept(float progress, int completed, TimeSpan now)
    {
        _progress = Math.Clamp(progress, 0f, 1f);
        if (completed > _completed)
            _completionUntil = now + CompletionDuration;
        _completed = completed;
    }

    public float ValueAt(TimeSpan now) => now < _completionUntil ? 1f : _progress;

    public void Reset()
    {
        _completed = 0;
        _progress = 0;
        _completionUntil = TimeSpan.Zero;
    }
}
