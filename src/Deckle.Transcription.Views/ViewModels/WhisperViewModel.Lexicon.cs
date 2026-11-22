using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Localization;
using Deckle.Transcription.Lexicon;

namespace Deckle.Transcription.Views;

// The dictation lexicon (TranscriptionSettings.Lexicon): the person's own
// words, one per line, the decoder boost that favors them and its strength.
//
// One fold with no master on the General page, under the speech engine: the
// list stands on its own (its casing pass always runs), and the strength only
// shows while the boost is on. The fold shows while Parakeet is the chosen
// engine, since Whisper reads its initial prompt instead; the autocorrect reads
// the same list whatever the engine.
//
// The list is held as one text, a term per line, the shape the multi-line field
// edits. It is cleaned on the way to the settings (LexiconTerms.Merge) and
// shown back as stored, so a duplicate or a blank line disappears once the
// field is committed.
public partial class WhisperViewModel
{
    [ObservableProperty]
    public partial string LexiconText { get; set; }

    [ObservableProperty]
    public partial bool LexiconBoostEnabled { get; set; }

    [ObservableProperty]
    public partial double LexiconBoostStrength { get; set; }

    // Seeds from the POCO initializer, its single source; called by the
    // constructor while _isSyncing holds, overwritten by Load().
    private void SeedLexicon()
    {
        var lexicon = new LexiconSettings();
        LexiconText = "";
        LexiconBoostEnabled = lexicon.BoostEnabled;
        LexiconBoostStrength = lexicon.BoostStrength;
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

    partial void OnLexiconBoostStrengthChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    public IReadOnlyList<SettingDescriptor> LexiconSettingsManifest =>
    [
        Setting.Section("WhisperLexiconFold",
            [
                Setting.Text("WhisperLexiconTermsCard",
                    () => LexiconText,
                    value => LexiconText = value,
                    new TextArgs(
                        Placeholder: Loc.GetFromOptional("Deckle.Transcription.Views", "WhisperLexiconTermsBox/PlaceholderText"),
                        Multiline: true),
                    defaultValue: () => ""),
                Setting.Toggle("WhisperLexiconBoostCard",
                    () => LexiconBoostEnabled,
                    value => LexiconBoostEnabled = value,
                    defaultValue: () => new LexiconSettings().BoostEnabled),
                // 0.5..5: the grain is 0.2, which writes the default 2 exactly.
                Setting.Magnitude("WhisperLexiconStrengthCard",
                    () => LexiconBoostStrength,
                    value => LexiconBoostStrength = value,
                    new MagnitudeArgs(LexiconSettings.MinBoostStrength, LexiconSettings.MaxBoostStrength),
                    visibleWhen: () => LexiconBoostEnabled,
                    defaultValue: () => LexiconSettings.DefaultBoostStrength),
            ],
            visibleWhen: () => !IsWhisperChosen),
    ];

    private void LoadLexicon(TranscriptionSettings s)
    {
        LexiconSettings lexicon = s.Lexicon ?? new LexiconSettings();
        LexiconText = string.Join(Environment.NewLine, LexiconTerms.Merge(lexicon.Terms));
        LexiconBoostEnabled = lexicon.BoostEnabled;
        LexiconBoostStrength = lexicon.BoostStrength;
    }

    private void PushLexicon(TranscriptionSettings s)
    {
        s.Lexicon ??= new LexiconSettings();
        s.Lexicon.Terms = [.. LexiconTerms.Merge(
            (LexiconText ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))];
        s.Lexicon.BoostEnabled = LexiconBoostEnabled;
        if (!double.IsNaN(LexiconBoostStrength))
            s.Lexicon.BoostStrength = LexiconBoostStrength;
    }
}
