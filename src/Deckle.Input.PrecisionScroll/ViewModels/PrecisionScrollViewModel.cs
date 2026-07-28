using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Input.PrecisionScroll;

public partial class PrecisionScrollViewModel : ObservableObject
{
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    public PrecisionScrollViewModel() => Load();

    public void Load()
    {
        _isSyncing = true;
        try
        {
            PrecisionScrollSettings settings = PrecisionScrollSettingsService.Instance.Current;
            Enabled = settings.Enabled;
            LoadTuning((settings.Tuning ?? new PrecisionScrollTuning()).Normalize());
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

    private void Save()
    {
        PrecisionScrollSettings settings = PrecisionScrollSettingsService.Instance.Current;
        settings.Enabled = Enabled;
        PrecisionScrollTuning requested = CreateTuning();
        PrecisionScrollTuning normalized = requested.Normalize();
        settings.Tuning = normalized;

        if (requested != normalized)
        {
            _isSyncing = true;
            try { LoadTuning(normalized); }
            finally { _isSyncing = false; }
        }

        PrecisionScrollSettingsService.Instance.Save();
    }
}
