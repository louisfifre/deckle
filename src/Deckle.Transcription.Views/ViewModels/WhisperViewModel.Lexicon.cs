using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Localization;
using Deckle.Transcription.Lexicon;

namespace Deckle.Transcription.Views;

// The dictation lexicon (TranscriptionSettings.Lexicon): the person's own
// words, one per line, and the switch of the decoder boost that favors them.
// Composed on its own page, Transcription > Lexicon (LexiconPage).
//
// The Words card declares no default: the list is the person's data, not a
// setting, so no reset (of the card or of a page) can ever empty it. The list
// shows under every engine, because the autocorrect reads it whatever the
// engine; only the switch is gated on Parakeet, the one engine that boosts.
//
// The list is held as one text, a term per line, the shape the multi-line field
// edits. It is cleaned on the way to the settings (LexiconTerms.Merge) and
// shown back as stored, so a duplicate, a one-letter entry or a blank line
// disappears once the field is committed; the card's description says so.
public partial class WhisperViewModel
{
    [ObservableProperty]
    public partial string LexiconText { get; set; }

    [ObservableProperty]
    public partial bool LexiconBoostEnabled { get; set; }

    // Seeds from the POCO initializer, its single source; called by the
    // constructor while _isSyncing holds, overwritten by Load().
    private void SeedLexicon()
    {
        LexiconText = "";
        LexiconBoostEnabled = new LexiconSettings().BoostEnabled;
    }

    partial void OnLexiconTextChanged(string value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnLexiconBoostEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    public IReadOnlyList<SettingDescriptor> LexiconSettingsManifest =>
    [
        Setting.Text("LexiconTermsCard",
            () => LexiconText,
            value => LexiconText = value,
            new TextArgs(
                Placeholder: Loc.GetFromOptional("Deckle.Transcription.Views", "LexiconTermsBox/PlaceholderText"),
                Multiline: true)),
        Setting.Toggle("LexiconBoostCard",
            () => LexiconBoostEnabled,
            value => LexiconBoostEnabled = value,
            visibleWhen: () => !IsWhisperChosen,
            defaultValue: () => new LexiconSettings().BoostEnabled),
    ];

    private void LoadLexicon(TranscriptionSettings s)
    {
        LexiconSettings lexicon = s.Lexicon ?? new LexiconSettings();
        LexiconText = string.Join(Environment.NewLine, LexiconTerms.Merge(lexicon.Terms));
        LexiconBoostEnabled = lexicon.BoostEnabled;
    }

    private void PushLexicon(TranscriptionSettings s)
    {
        s.Lexicon ??= new LexiconSettings();
        s.Lexicon.Terms = [.. LexiconTerms.Merge(
            (LexiconText ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))];
        s.Lexicon.BoostEnabled = LexiconBoostEnabled;
    }
}
