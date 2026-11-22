using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deckle.Catalog;
using Deckle.Transcription.Lexicon;

namespace Deckle.Transcription.Views;

// The dictation lexicon (TranscriptionSettings.Lexicon): the person's own
// words, added one at a time, and the switch of the decoder boost that favors
// them. Shown on its own page, Transcription > Lexicon (LexiconPage).
//
// The words are the person's data, not a setting: they have no default and no
// reset, so nothing but their own remove gesture takes one away. The
// autocorrect reads the list too, whether or not the boost is on.
//
// An added word goes through LexiconTerms.Merge with the list, the one cleaning
// rule: a duplicate (any casing) or a one-letter entry adds nothing.
public partial class TranscriptionViewModel
{
    public ObservableCollection<LexiconWordRow> LexiconWords { get; } = new();

    // What the person is typing into the add field. Not persisted: it becomes a
    // word only on the add gesture.
    [ObservableProperty]
    public partial string NewLexiconWord { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool LexiconBoostEnabled { get; set; }

    // Seeds from the POCO initializer, its single source; called by the
    // constructor while _isSyncing holds, overwritten by Load().
    private void SeedLexicon()
    {
        LexiconBoostEnabled = new LexiconSettings().BoostEnabled;
    }

    partial void OnLexiconBoostEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    public IReadOnlyList<SettingDescriptor> LexiconSettingsManifest =>
    [
        Setting.Toggle("LexiconBoostCard",
            () => LexiconBoostEnabled,
            value => LexiconBoostEnabled = value,
            defaultValue: () => new LexiconSettings().BoostEnabled),
    ];

    [RelayCommand]
    private void AddLexiconWord()
    {
        var merged = LexiconTerms.Merge(LexiconWords.Select(row => row.Word), [NewLexiconWord ?? ""]);
        NewLexiconWord = string.Empty;
        if (merged.Count == LexiconWords.Count) return;

        LexiconWords.Add(new LexiconWordRow(merged[^1], OnLexiconWordRemoved));
        PushToSettings();
    }

    private void OnLexiconWordRemoved(LexiconWordRow row)
    {
        LexiconWords.Remove(row);
        PushToSettings();
    }

    private void LoadLexicon(TranscriptionSettings s)
    {
        LexiconSettings lexicon = s.Lexicon ?? new LexiconSettings();
        LexiconWords.Clear();
        foreach (string word in LexiconTerms.Merge(lexicon.Terms))
            LexiconWords.Add(new LexiconWordRow(word, OnLexiconWordRemoved));
        LexiconBoostEnabled = lexicon.BoostEnabled;
    }

    private void PushLexicon(TranscriptionSettings s)
    {
        s.Lexicon ??= new LexiconSettings();
        s.Lexicon.Terms = [.. LexiconWords.Select(row => row.Word)];
        s.Lexicon.BoostEnabled = LexiconBoostEnabled;
    }
}
