namespace Deckle.Hud;

// The latest capture snapshot can replace an older one without losing a cut.
// Holds a full gauge briefly so a cut remains visible after immediate speech.
internal sealed class UtteranceCountdownState
{
    internal static readonly TimeSpan CompletionDuration = TimeSpan.FromMilliseconds(300);
    private int _completed;
    private float _progress;
    private TimeSpan _completionUntil;

    public void Accept(float progress, int completed, TimeSpan now)
    {
        _progress = Math.Clamp(progress, 0f, 1f);
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
