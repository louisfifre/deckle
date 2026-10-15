using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Llm.Rewrite;

// ─── LlmGeneralViewModel ────────────────────────────────────────────────────
//
// The observable half of the Rewriting page's module switch. SettingsComposer
// needs an INotifyPropertyChanged source it can re-read on PropertyChanged;
// LlmSettingsService owns the persistence.
public sealed partial class LlmGeneralViewModel : ObservableObject
{
    // Suppresses the change handler while Load() seeds the property, so a
    // hydration does not re-persist.
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    public LlmGeneralViewModel()
    {
        _isSyncing = true;
        Load();
    }

    // Called on every page navigation too: the page-level Reset all may have
    // replaced the settings while the cached page sat hidden.
    public void Load()
    {
        _isSyncing = true;
        try
        {
            Enabled = LlmSettingsService.Instance.Current.Enabled;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        LlmSettingsService.Instance.Current.Enabled = value;
        LlmSettingsService.Instance.Save();
    }
}
