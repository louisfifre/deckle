namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── Visibility — the one decision site, idempotent ────────────────────
    //
    // A band shows when the layout is known, nothing fullscreen sits on its
    // monitor, the machine is awake and unlocked, and the cursor leaves it
    // some opacity. The band itself performs the show or hide.

    private void UpdateCover(CoverBand band, string reason)
    {
        bool shouldBeVisible = _layoutKnown && !band.AppSuppressed
                            && !_systemSuspended && band.Alpha > 0;
        if (shouldBeVisible == band.Visible) return;

        if (shouldBeVisible) band.Show(reason);
        else                 band.Hide(reason);
    }
}
