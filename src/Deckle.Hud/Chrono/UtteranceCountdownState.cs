using Deckle.Composition;

namespace Deckle.Hud;

// The latest capture snapshot can replace an older one without losing a cut.
// Holds a full gauge briefly so a cut remains visible after immediate speech.
//
// Samples stay raw here. The smoothing is the visual's motion, one named
// duration for every move (UtteranceCountdownVisual.SetProgress), not a
// filter on the data: a filter would hide the capture's timing behind an
// audio constant, where a motion keeps the gauge in step with the rest of
// the HUD's animations.
internal sealed class UtteranceCountdownState
{
    internal static readonly TimeSpan CompletionDuration = TimeSpan.FromMilliseconds(300);
    private int _completed;
    private float _progress;
    private TimeSpan _completionUntil;
    private bool _cutPending;

    public void Accept(float progress, int completed, TimeSpan now)
    {
        _progress = Math.Clamp(progress, 0f, 1f);
        if (completed > _completed)
        {
            _completionUntil = now + CompletionDuration;
            _cutPending = true;
        }
        _completed = completed;
    }

    public float ValueAt(TimeSpan now) => now < _completionUntil ? 1f : _progress;

    // Which move to rest the renderer plays next. The first move after a
    // cut's hold is the cut's release, taken once: the renderer asks only
    // when the value has changed, so the answer waits on the hold and is
    // spent by the move that leaves it. Every other move to rest is speech
    // resuming, the retreat.
    public UtteranceCountdownRelease TakeRelease(TimeSpan now)
    {
        if (!_cutPending || now < _completionUntil) return UtteranceCountdownRelease.Retreat;
        _cutPending = false;
        return UtteranceCountdownRelease.Cut;
    }

    public void Reset()
    {
        _completed = 0;
        _progress = 0;
        _completionUntil = TimeSpan.Zero;
        _cutPending = false;
    }
}
