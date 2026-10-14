using Deckle.Shell.TaskbarCover;

namespace Deckle.App;

public partial class App
{
    private TaskbarCoverHost? _taskbarCover;

    private void InitializeTaskbarCover()
    {
        // The watcher is already started, on this same UI thread; the cover
        // only subscribes to it.
        _taskbarCover = new TaskbarCoverHost(_foregroundWatcher!);

        TaskbarCoverSettingsService.Instance.Changed += ReconcileTaskbarCover;
        ReconcileTaskbarCover();
    }

    // Start and Stop are idempotent, so unrelated settings changes cost nothing.
    // The approach level is relayed first, so a Start builds its zone at the
    // saved level; on a running host a level that differs from the one it
    // holds posts WM_APP_APPROACH to the cover thread, an equal one does nothing.
    private void ReconcileTaskbarCover()
    {
        if (_taskbarCover is null) return;

        TaskbarCoverSettings settings = TaskbarCoverSettingsService.Instance.Current;
        _taskbarCover.Approach = settings.Approach;

        if (settings.Enabled) _taskbarCover.Start();
        else                  _taskbarCover.Stop();
    }

    // Unhook before disposing: a debounced settings flush landing during
    // shutdown would otherwise restart the host behind Dispose. The flush
    // then persists a toggle still sitting in the debounce window — QuitApp
    // only flushes the main settings store, never the module stores.
    private void ShutdownTaskbarCover()
    {
        TaskbarCoverSettingsService.Instance.Changed -= ReconcileTaskbarCover;
        TaskbarCoverSettingsService.Instance.Flush();
        _taskbarCover?.Dispose();
    }
}
