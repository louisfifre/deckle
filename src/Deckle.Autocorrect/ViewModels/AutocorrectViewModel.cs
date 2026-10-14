using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deckle.Autocorrect;

// ViewModel for AutocorrectPage. Load() seeds the bound state with writes
// suppressed (_isSyncing), then each user change routes back through the
// service that owns it.
public sealed partial class AutocorrectViewModel : ObservableObject
{
    private bool _isSyncing;

    // Master switch — when off, the engine corrects nothing, in any app.
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // Words the user pulled out of correction's reach, whatever lexicon carried
    // them.
    public ObservableCollection<AutocorrectExcludedWordRow> ExcludedWords { get; } = new();

    // What the user is typing into the exclusion field. Not persisted — it
    // becomes an entry only on the add gesture.
    [ObservableProperty]
    public partial string NewExclusion { get; set; } = string.Empty;

    // The module's dataset consents live in AutocorrectTelemetryViewModel, its
    // contribution to the Diagnostics page.

    public AutocorrectViewModel()
    {
        _isSyncing = true;
        Load();
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var settings = AutocorrectSettingsService.Instance.Current;
            Enabled = settings.Enabled;

            ExcludedWords.Clear();
            foreach (string word in settings.ExcludedWords)
                ExcludedWords.Add(new AutocorrectExcludedWordRow(word, OnWordIncluded));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        AutocorrectSettingsService.Instance.SetEnabled(value);
    }

    // The service owns the normalization and answers with the form it
    // registered, so the list shows the stored key rather than what was typed.
    // Text that cannot name a single word — blank, or several words — clears
    // the field and adds nothing, with no error message.
    [RelayCommand]
    private void ExcludeWord()
    {
        string? excluded = AutocorrectSettingsService.Instance.ExcludeWord(NewExclusion);
        NewExclusion = string.Empty;
        if (excluded is null) return;

        if (ExcludedWords.Any(row => string.Equals(row.Word, excluded, StringComparison.Ordinal)))
            return;

        int index = 0;
        while (index < ExcludedWords.Count
               && string.CompareOrdinal(ExcludedWords[index].Word, excluded) < 0)
            index++;
        ExcludedWords.Insert(index, new AutocorrectExcludedWordRow(excluded, OnWordIncluded));
    }

    private void OnWordIncluded(AutocorrectExcludedWordRow row)
    {
        AutocorrectSettingsService.Instance.IncludeWord(row.Word);
        ExcludedWords.Remove(row);
    }
}
