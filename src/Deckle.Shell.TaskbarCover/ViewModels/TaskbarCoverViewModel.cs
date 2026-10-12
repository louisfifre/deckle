using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Shell.TaskbarCover;

// The page's only state is the master switch. Saving it is enough: the
// App's settings observer starts or stops the cover host, exactly as it does
// when the tray switch or the hotkey flips the same flag.
public partial class TaskbarCoverViewModel : ObservableObject
{
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    public TaskbarCoverViewModel() => Load();

    public void Load()
    {
        _isSyncing = true;
        try
        {
            Enabled = TaskbarCoverSettingsService.Instance.Current.Enabled;
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
}
