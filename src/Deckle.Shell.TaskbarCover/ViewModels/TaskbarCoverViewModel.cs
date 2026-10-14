using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Shell.TaskbarCover;

// The page's state is the master switch and the approach level. Saving is
// enough: the App's settings observer starts or stops the cover host and
// relays the level to it, exactly as it does when the tray switch or the
// hotkey flips the same flag.
public partial class TaskbarCoverViewModel : ObservableObject
{
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial ApproachDistance Approach { get; set; }

    public TaskbarCoverViewModel() => Load();

    public void Load()
    {
        _isSyncing = true;
        try
        {
            TaskbarCoverSettings settings = TaskbarCoverSettingsService.Instance.Current;
            Enabled = settings.Enabled;
            Approach = settings.Approach;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        TaskbarCoverSettingsService.Instance.Current.Enabled = value;
        TaskbarCoverSettingsService.Instance.Save();
    }

    partial void OnApproachChanged(ApproachDistance value)
    {
        if (_isSyncing) return;
        TaskbarCoverSettingsService.Instance.Current.Approach = value;
        TaskbarCoverSettingsService.Instance.Save();
    }
}
