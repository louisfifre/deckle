using Deckle.Audio;

namespace Deckle.Hud;

// The latest capture snapshot can replace an older one without losing a cut.
// Holds a full gauge briefly so a cut remains visible after immediate speech.
//
// A pause rises through the Recording outline's own smoother: the level EMA
// of AudioLevelMapper.EmaAlpha, applied per capture sample (both arrive on
// the same 50 ms frames), so the gauge that replaces the outline settles at
// the outline's pace and the two are tuned by one value. It rounds the sample
// grid and the onset, nothing slower — on a ramp the lag is well under one
// sample. Resumed speech is not smoothed: the value snaps to 0 so
// UtteranceCountdownVisual plays its designed retreat from wherever the gauge
// stood.
internal sealed class UtteranceCountdownState
{
    internal static readonly TimeSpan CompletionDuration = TimeSpan.FromMilliseconds(300);
    private int _completed;
    private float _progress;
    private TimeSpan _completionUntil;

    public void Accept(float progress, int completed, TimeSpan now)
    {
        float target = Math.Clamp(progress, 0f, 1f);
        float keep = AudioLevelMapper.EmaAlpha;
        _progress = target > 0f ? _progress * keep + target * (1f - keep) : 0f;
        if (completed > _completed) _completionUntil = now + CompletionDuration;
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
