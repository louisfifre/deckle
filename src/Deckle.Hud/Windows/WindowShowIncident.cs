namespace Deckle.Hud;

// The degradation of one HUD window kind's topmost raise, reported as one
// incident: the first refused SetWindowPos opens a Warning with its Win32
// error, later refusals only count, and the next accepted raise closes it.
// Each show repeats the call, so a refusal that persists would otherwise
// write a Warning per dictation.
//
// The lock is there because the overlay cards share one instance and nothing
// in this type should depend on every window living on the same thread.
internal sealed class WindowShowIncident
{
    private readonly string _window;
    private readonly object _sync = new();
    private int _failures;

    public WindowShowIncident(string window) => _window = window;

    public void Observe(bool ok, int win32Error, string stage)
    {
        bool open;
        int failures;
        lock (_sync)
        {
            if (ok)
            {
                if (_failures == 0) return;
                failures = _failures;
                _failures = 0;
                open = false;
            }
            else
            {
                failures = ++_failures;
                open = failures == 1;
                if (!open) return;
            }
        }

        if (open)
            DeckleHudSource.Log.HudShowFailed(_window, stage, win32Error);
        else
            DeckleHudSource.Log.HudShowRecovered(_window, failures);
    }
}
