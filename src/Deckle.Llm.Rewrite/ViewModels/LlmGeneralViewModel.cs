using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Llm.Rewrite;

// ─── LlmGeneralViewModel ────────────────────────────────────────────────────
//
// The observable half of the Rewriting page's General section: the "Enable
// rewriting" master switch and the "Ollama endpoint" URL. SettingsComposer
// needs an INotifyPropertyChanged source it can re-read on PropertyChanged;
// LlmSettingsService owns the persistence.
public sealed partial class LlmGeneralViewModel : ObservableObject
{
    // Suppresses the change handlers while Load() seeds the properties, so a
    // hydration neither re-persists nor fires EndpointChanged.
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string OllamaEndpoint { get; set; } = "";

    // Raised on every persisted endpoint edit so LlmPage re-probes Ollama
    // availability against the new URL.
    public event EventHandler? EndpointChanged;

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
            var s = LlmSettingsService.Instance.Current;
            Enabled = s.Enabled;
            OllamaEndpoint = s.OllamaEndpoint;
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

    partial void OnOllamaEndpointChanged(string value)
    {
        if (_isSyncing) return;
        // The trim is written to the settings only, not back onto the property,
        // so the composed TextBox is not fought mid-edit.
        LlmSettingsService.Instance.Current.OllamaEndpoint = value.Trim();
        LlmSettingsService.Instance.Save();
        EndpointChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToDefaults()
    {
        var defaults = new LlmSettings();
        var s = LlmSettingsService.Instance.Current;
        s.Enabled = defaults.Enabled;
        s.OllamaEndpoint = defaults.OllamaEndpoint;
        LlmSettingsService.Instance.Save();
        Load();
        EndpointChanged?.Invoke(this, EventArgs.Empty);
    }
}
