using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace Deckle.Shell.Taskbar;

// The page's state. ShowAllTrayIcons is a setting: every change writes it and
// saves; applying it belongs to the App, which observes the store and drives the
// promoter, exactly as it does for the cover. The page only reads back how the
// promoter's last pass ended.
public partial class TaskbarViewModel : ObservableObject
{
    // Raised so the composer re-reads every advisory and every switch; no
    // binding reads it.
    public const string StatusPropertyName = "Status";

    private readonly TrayIconPromoter _tray;
    private bool _isSyncing;
    private DispatcherQueue? _dispatcher;

    [ObservableProperty]
    public partial bool ShowAllTrayIcons { get; set; }

    public TaskbarViewModel()
        : this(TrayIconPromoter.Instance)
    {
    }

    public TaskbarViewModel(TrayIconPromoter tray)
    {
        _tray = tray;
        Load();
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            ShowAllTrayIcons = TaskbarSettingsService.Instance.Current.ShowAllTrayIcons;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnShowAllTrayIconsChanged(bool value)
    {
        if (_isSyncing) return;
        TaskbarSettingsService.Instance.Current.ShowAllTrayIcons = value;
        TaskbarSettingsService.Instance.Save();
    }

    /// <summary>
    /// Starts following the tray promoter's outcome. The promoter is
    /// process-wide and outlives the page, so the page attaches on Loaded and
    /// detaches on Unloaded, and a closed Settings window leaves no
    /// subscriber behind. The outcome changes on any thread and is raised on
    /// <paramref name="dispatcher"/>.
    /// </summary>
    public void AttachStatus(DispatcherQueue dispatcher)
    {
        if (_dispatcher is not null) return;
        _dispatcher = dispatcher;
        _tray.OutcomeChanged += OnTrayOutcomeChanged;
        OnPropertyChanged(StatusPropertyName);
    }

    public void DetachStatus()
    {
        if (_dispatcher is null) return;
        _tray.OutcomeChanged -= OnTrayOutcomeChanged;
        _dispatcher = null;
    }

    private void OnTrayOutcomeChanged()
    {
        DispatcherQueue? dispatcher = _dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.HasThreadAccess) OnPropertyChanged(StatusPropertyName);
        else dispatcher.TryEnqueue(() => OnPropertyChanged(StatusPropertyName));
    }
}