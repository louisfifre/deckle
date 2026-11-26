namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── The taskbar's alpha — the one decision site, idempotent ───────────
    //
    // A taskbar fades when the layout is known, nothing fullscreen sits on its
    // monitor, it does not hold the focus, and the machine is awake and
    // unlocked; it then takes the alpha its band's level gives. Otherwise it
    // stays as Explorer made it.

    private void UpdateCover(CoverBand band, string reason)
    {
        bool active = _layoutKnown && !band.AppSuppressed && !band.TaskbarFocused
                   && !_systemSuspended;
        byte alpha = active ? TaskbarFade.AlphaFor(band.Level) : TaskbarFade.Opaque;
        ApplyTaskbarAlpha(band, alpha, reason);
    }

    // Writes the alpha, logs the band's turns between whole and faded, keeps
    // the crash net's list current and opens or closes the write incident.
    private void ApplyTaskbarAlpha(CoverBand band, byte alpha, string reason)
    {
        bool wasFaded = band.Faded;
        int error = band.ApplyTaskbarAlpha(alpha);

        if (error != 0)
        {
            if (!_fadeFailureLogged)
            {
                DeckleShellTaskbarCoverSource.Log.FadeFailed(band.Index, error);
                _fadeFailureLogged = true;
            }
            return;
        }
        if (_fadeFailureLogged && alpha != TaskbarFade.Opaque)
        {
            DeckleShellTaskbarCoverSource.Log.FadeRecovered();
            _fadeFailureLogged = false;
        }

        if (band.Faded == wasFaded) return;
        if (band.Faded) DeckleShellTaskbarCoverSource.Log.TaskbarFaded(band.Index, reason);
        else            DeckleShellTaskbarCoverSource.Log.TaskbarRestored(band.Index, reason);
        PublishFaded();
    }

    // Gives every faded taskbar back as Explorer made it. Worker thread.
    private void RestoreAll(string reason)
    {
        foreach (var band in _bands) ApplyTaskbarAlpha(band, TaskbarFade.Opaque, reason);
    }

    // ── Crash net ─────────────────────────────────────────────────────────
    //
    // The taskbar's alpha lives on Explorer's window, so a Deckle that dies
    // with a taskbar faded would leave it invisible. The worker publishes the
    // faded taskbars here; an unhandled exception or the process exit restores
    // them from whatever thread it runs on. A process killed from outside
    // runs neither: the next Start fades from the cursor again, and a Deckle
    // that starts with the cover off restores them (RestoreLeftovers).

    private volatile IntPtr[] _fadedTaskbars = [];

    private void PublishFaded()
        => _fadedTaskbars = _bands.Where(b => b.Faded).Select(b => b.Taskbar).ToArray();

    private void OnProcessEnding(object? sender, EventArgs e)
    {
        foreach (var taskbar in _fadedTaskbars) TaskbarFade.Restore(taskbar);
    }

    /// <summary>
    /// Gives back every taskbar a previous Deckle left faded: one killed from
    /// outside runs no exit handler. Called when the cover starts off. Any
    /// thread.
    /// </summary>
    public static void RestoreLeftovers()
    {
        foreach (var placement in TaskbarLocator.Locate()) TaskbarFade.Restore(placement.Taskbar);
    }
}
