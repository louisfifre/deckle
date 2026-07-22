using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Input.PrecisionScroll;

public partial class PrecisionScrollViewModel : ObservableObject
{
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial double Sensitivity { get; set; }

    public PrecisionScrollViewModel() => Load();

    public void Load()
    {
        _isSyncing = true;
        try
        {
            PrecisionScrollSettings settings = PrecisionScrollSettingsService.Instance.Current;
            Enabled = settings.Enabled;
            Sensitivity = Math.Clamp(settings.Sensitivity, 0.5, 2.0);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_isSyncing) Save();
    }

    partial void OnSensitivityChanged(double value)
    {
        if (!_isSyncing) Save();
    }

    private void Save()
    {
        PrecisionScrollSettings settings = PrecisionScrollSettingsService.Instance.Current;
        settings.Enabled = Enabled;
        settings.Sensitivity = Math.Round(Sensitivity, 2);
        PrecisionScrollSettingsService.Instance.Save();
    }
}
