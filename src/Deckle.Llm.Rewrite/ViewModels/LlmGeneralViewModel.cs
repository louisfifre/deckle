using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Llm.Rewrite;

// ─── LlmGeneralViewModel ────────────────────────────────────────────────────
//
// The observable half of the Rewriting page's General section — the ONLY two
// cleanly-composable leaves of the whole page: the "Enable rewriting" master
// switch and the "Ollama endpoint" URL. Everything else on the page (profiles,
// rules, models, shortcut slots) stays hand-authored, being dynamic runtime
// collections no composer kind models.
//
// Why a ViewModel now. The section used to read/write LlmSettingsService.Current
// (a plain POCO) directly from the UserControl's code-behind. SettingsComposer
// needs an INotifyPropertyChanged source whose properties it can re-read on
// PropertyChanged, so these two values move onto an ObservableObject — the same
// shape AutocorrectViewModel uses for its master switch. Load() seeds the state
// with writes suppressed; each user change routes back through
// LlmSettingsService, which owns the persistence.
//
// EndpointChanged is re-raised here (not on the UserControl any more) so the page
// still re-checks Ollama availability when the URL changes — availability depends
// on the URL, so a URL edit invalidates the cached probe.
public sealed partial class LlmGeneralViewModel : ObservableObject
{
    // Suppresses the change handlers while Load() seeds the properties from the
    // persisted settings — the same _isSyncing shape AutocorrectViewModel uses,
    // so a hydration does not re-persist or re-fire EndpointChanged.
    private bool _isSyncing;

    // Master switch — when off, the engine rewrites nothing and the page masks
    // (collapses) every dependent section. The page observes this property's
    // PropertyChanged to drive that masking, so the composed toggle's write flows
    // straight through to the gating.
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // The URL of the local Ollama instance. Free-form text (a URL string, not a
    // folder), composed as Setting.Text.
    [ObservableProperty]
    public partial string OllamaEndpoint { get; set; } = "";

    // Re-raised on every persisted endpoint edit so LlmPage re-probes Ollama
    // availability against the new URL. Named the same as the event it replaces
    // on LlmGeneralSection, so the page's wiring is unchanged bar the sender.
    public event EventHandler? EndpointChanged;

    public LlmGeneralViewModel()
    {
        _isSyncing = true;
        Load();
    }

    // Re-pull the two values from the live settings with writes suppressed. Called
    // at construction and on every page navigation (a Reset elsewhere, or the
    // page-level Reset all, may have replaced the settings while the page sat
    // cached).
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
        // Persist the trimmed URL — the same normalization the hand-authored
        // TextChanged applied. The trim is written to the settings only, not back
        // onto the property, so the composed TextBox is not fought mid-edit.
        LlmSettingsService.Instance.Current.OllamaEndpoint = value.Trim();
        LlmSettingsService.Instance.Save();
        EndpointChanged?.Invoke(this, EventArgs.Empty);
    }

    // Restore the two values to their defaults (a fresh LlmSettings — no literal
    // duplication here) and re-probe Ollama. Persists, then re-seeds the bound
    // state through Load() so the composed cards reflect the reset.
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
