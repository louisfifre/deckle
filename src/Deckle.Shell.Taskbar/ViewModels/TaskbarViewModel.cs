using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace Deckle.Shell.Taskbar;

// The page's state. Every value is a setting: a change writes it and saves, and
// applying it belongs to the App, which observes the store. The tray promoter
// carries "show all tray icons", WindowsKeyTaskbarFocus the Windows key and
// TaskbarCustomizer the rest. The page only reads back how each ended: the
// promoter's last pass, and the parts the engine could not put in effect.
public partial class TaskbarViewModel : ObservableObject
{
    // Raised so the composer re-reads every advisory and every switch; no
    // binding reads it.
    public const string StatusPropertyName = "Status";

    private readonly TrayIconPromoter _tray;
    private readonly TaskbarCustomizer _customizer;
    private readonly Func<bool> _isBarVertical;
    private bool _isSyncing;
    private DispatcherQueue? _dispatcher;

    [ObservableProperty]
    public partial bool ShowAllTrayIcons { get; set; }

    [ObservableProperty]
    public partial TaskbarBackground Background { get; set; }

    [ObservableProperty]
    public partial bool CompactTray { get; set; }

    [ObservableProperty]
    public partial bool CompactTaskButtons { get; set; }

    [ObservableProperty]
    public partial bool CompactClock { get; set; }

    [ObservableProperty]
    public partial bool StartMenuFullHeight { get; set; }

    [ObservableProperty]
    public partial bool WindowsKeyShowsTaskbar { get; set; }

    public TaskbarViewModel()
        : this(TrayIconPromoter.Instance, TaskbarCustomizer.Instance, TaskbarOrientation.IsVertical)
    {
    }

    internal TaskbarViewModel(TrayIconPromoter tray, TaskbarCustomizer customizer, Func<bool> isBarVertical)
    {
        _tray = tray;
        _customizer = customizer;
        _isBarVertical = isBarVertical;
        Load();
    }

    public void Load()
    {
        TaskbarSettings settings = TaskbarSettingsService.Instance.Current;
        _isSyncing = true;
        try
        {
            ShowAllTrayIcons = settings.ShowAllTrayIcons;
            Background = settings.Background;
            CompactTray = settings.CompactTray;
            CompactTaskButtons = settings.CompactTaskButtons;
            CompactClock = settings.CompactClock;
            StartMenuFullHeight = settings.StartMenuFullHeight;
            WindowsKeyShowsTaskbar = settings.WindowsKeyShowsTaskbar;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnShowAllTrayIconsChanged(bool value) => Write(settings => settings.ShowAllTrayIcons = value);
    partial void OnBackgroundChanged(TaskbarBackground value) => Write(settings => settings.Background = value);
    partial void OnCompactTrayChanged(bool value) => Write(settings => settings.CompactTray = value);
    partial void OnCompactTaskButtonsChanged(bool value) => Write(settings => settings.CompactTaskButtons = value);
    partial void OnCompactClockChanged(bool value) => Write(settings => settings.CompactClock = value);
    partial void OnStartMenuFullHeightChanged(bool value) => Write(settings => settings.StartMenuFullHeight = value);
    partial void OnWindowsKeyShowsTaskbarChanged(bool value) => Write(settings => settings.WindowsKeyShowsTaskbar = value);

    private void Write(Action<TaskbarSettings> change)
    {
        if (_isSyncing) return;
        change(TaskbarSettingsService.Instance.Current);
        TaskbarSettingsService.Instance.Save();
    }

    /// <summary>
    /// Starts following the outcome of the tray promoter and of the engines.
    /// Both are process-wide and outlive the page, so the page attaches on
    /// Loaded and detaches on Unloaded, and a closed Settings window leaves no
    /// subscriber behind. The outcome changes on any thread and is raised on
    /// <paramref name="dispatcher"/>.
    /// </summary>
    public void AttachStatus(DispatcherQueue dispatcher)
    {
        if (_dispatcher is not null) return;
        _dispatcher = dispatcher;
        _tray.OutcomeChanged += OnOutcomeChanged;
        _customizer.ProblemsChanged += OnOutcomeChanged;
        OnPropertyChanged(StatusPropertyName);
    }

    public void DetachStatus()
    {
        if (_dispatcher is null) return;
        _tray.OutcomeChanged -= OnOutcomeChanged;
        _customizer.ProblemsChanged -= OnOutcomeChanged;
        _dispatcher = null;
    }

    private void OnOutcomeChanged()
    {
        DispatcherQueue? dispatcher = _dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.HasThreadAccess) OnPropertyChanged(StatusPropertyName);
        else dispatcher.TryEnqueue(() => OnPropertyChanged(StatusPropertyName));
    }
}
