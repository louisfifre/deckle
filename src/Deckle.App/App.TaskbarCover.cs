using Deckle.Shell.TaskbarCover;

namespace Deckle.App;

// Taskbar cover module composition. The App owns the host and keeps it
// reconciled with the persisted module settings, same posture as the
// trackpad composition: the dedicated thread only runs while the master
// switch is on.
public partial class App
{
    private TaskbarCoverHost? _taskbarCover;

    private void InitializeTaskbarCover()
    {
        _taskbarCover = new TaskbarCoverHost();

        TaskbarCoverSettingsService.Instance.Changed += ReconcileTaskbarCover;
        ReconcileTaskbarCover();
    }

    // Idempotent settings → runtime reconciliation, called at boot and on
    // every settings flush. Start/Stop on the host are themselves
    // idempotent, so re-running on unrelated settings changes costs nothing.
    private void ReconcileTaskbarCover()
    {
        if (_taskbarCover is null) return;

        if (TaskbarCoverSettingsService.Instance.Current.Enabled) _taskbarCover.Start();
        else                                                      _taskbarCover.Stop();
    }

    private void ShutdownTaskbarCover() => _taskbarCover?.Dispose();
}
