using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Llm.Rewrite;

// ─── LlmDictationViewModel ──────────────────────────────────────────────────
//
// The observable half of the Rewriting page's "While you dictate" section:
// rewriting batch by batch during a dictation (LlmSettings.BatchRewriting).
// SettingsComposer needs an INotifyPropertyChanged source it can re-read on
// PropertyChanged; LlmSettingsService owns the persistence. Each composed
// write lands here at the end of its gesture and is saved at once.
public sealed partial class LlmDictationViewModel : ObservableObject
{
    // Suppresses the change handlers while Load() seeds the properties, so a
    // hydration does not re-persist.
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // A double because the composer's magnitude travels as one; stored as an
    // int count of characters.
    [ObservableProperty]
    public partial double ThresholdChars { get; set; }

    public LlmDictationViewModel()
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
            BatchRewriteSettings batching = LlmSettingsService.Instance.Current.BatchRewriting;
            Enabled = batching.Enabled;
            ThresholdChars = batching.ThresholdChars;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        LlmSettingsService.Instance.Current.BatchRewriting.Enabled = value;
        LlmSettingsService.Instance.Save();
    }

    partial void OnThresholdCharsChanged(double value)
    {
        if (_isSyncing) return;
        // The composer already bounds the field; the clamp keeps a stray value
        // from ever reaching the batcher outside the range it was tuned for.
        int chars = (int)Math.Round(Math.Clamp(
            value,
            BatchRewriteSettings.MinThresholdChars,
            BatchRewriteSettings.MaxThresholdChars));
        LlmSettingsService.Instance.Current.BatchRewriting.ThresholdChars = chars;
        LlmSettingsService.Instance.Save();
    }
}
