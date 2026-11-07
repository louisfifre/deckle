using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Shell.TaskbarCover;

// The page's state is the master switch and the approach distance, in DIP.
// Saving is enough: the App's settings observer starts or stops the cover
// host and relays the distance to it, exactly as it does when the tray switch
// or the hotkey flips the same flag.
public partial class TaskbarCoverViewModel : ObservableObject
{
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial int ApproachDistanceDip { get; set; }

    // Each distance the approach slider shows during a drag, before the
    // release writes ApproachDistanceDip, for the preview to follow the
    // thumb. A plain event rather than an observable property: the settings
    // composer re-reads every row on any PropertyChanged of this model, and
    // a re-read mid-drag would pull the thumb back to the saved value.
    public event Action<int>? ApproachDistancePreviewed;

    public TaskbarCoverViewModel() => Load();

    public void Load()
    {
        _isSyncing = true;
        try
        {
            TaskbarCoverSettings settings = TaskbarCoverSettingsService.Instance.Current;
            Enabled = settings.Enabled;
            ApproachDistanceDip = settings.ApproachDistanceDip;
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

    partial void OnApproachDistanceDipChanged(int value)
    {
        if (_isSyncing) return;
        TaskbarCoverSettingsService.Instance.Current.ApproachDistanceDip = value;
        TaskbarCoverSettingsService.Instance.Save();
    }
}
