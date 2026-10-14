using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Transcription;

// ── WhisperTelemetryViewModel ─────────────────────────────────────────────────
//
// The dictation's dataset consents — latency timings and the audio corpus —
// as the module's contribution to Settings > Diagnostics, the one editor of
// everything Deckle records about itself (2026-10-14; the cards sat in a
// Diagnostics section of the Dictation page before). The module keeps the
// model and the consent dialogs; the Diagnostics page composes the cards
// through DiagnosticsRegistry and resolves their words from this assembly.
//
// Load() pulls from the telemetry store; a property change pushes the whole
// set back, and _isSyncing prevents re-saving during Load().
public sealed partial class WhisperTelemetryViewModel : ObservableObject
{
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool TelemetryLatencyEnabled { get; set; }

    [ObservableProperty]
    public partial bool TelemetryCorpusEnabled { get; set; }

    [ObservableProperty]
    public partial bool RecordAudioCorpus { get; set; }

    // Index in the RadioButtons order (0 = match the transcription, 1 = always
    // raw), cast to TelemetrySettings.AudioCorpusContent.
    [ObservableProperty]
    public partial int AudioCorpusContentIndex { get; set; }

    public WhisperTelemetryViewModel()
    {
        // Seeded closed under the guard, so no setter saves the seeds; Load()
        // overwrites them.
        _isSyncing = true;
        TelemetryLatencyEnabled = false;
        TelemetryCorpusEnabled = false;
        RecordAudioCorpus = false;
        AudioCorpusContentIndex = 0;
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var t = TelemetrySettingsService.Instance.Current;
            TelemetryLatencyEnabled = t.LatencyEnabled;
            TelemetryCorpusEnabled = t.CorpusEnabled;
            RecordAudioCorpus = t.RecordAudioCorpus;
            AudioCorpusContentIndex = (int)t.AudioCorpusContent;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnTelemetryLatencyEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        Push();
    }

    partial void OnTelemetryCorpusEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        Push();
    }

    partial void OnRecordAudioCorpusChanged(bool value)
    {
        if (_isSyncing) return;
        Push();
    }

    partial void OnAudioCorpusContentIndexChanged(int value)
    {
        // RadioButtons emits -1 transiently while it realises its items.
        if (_isSyncing || value < 0) return;
        Push();
    }

    private void Push()
    {
        var t = TelemetrySettingsService.Instance.Current;
        t.LatencyEnabled = TelemetryLatencyEnabled;
        t.CorpusEnabled = TelemetryCorpusEnabled;
        t.RecordAudioCorpus = RecordAudioCorpus;
        t.AudioCorpusContent = AudioCorpusContentIndex < 0
            ? AudioCorpusContent.MatchTranscription
            : (AudioCorpusContent)AudioCorpusContentIndex;
        TelemetrySettingsService.Instance.Save();
    }

    // The cards, in on-screen order: the latency toggle, then the audio-corpus
    // consent fold. The fold is a Group whose master and audio child each gate
    // their OFF→ON flip behind a consent dialog reached through the Catalog
    // registry (TelemetryConsent.RequestCorpus, .RequestAudioCorpus), so the
    // module asks without referencing the shell. The master → record → content
    // chain masks (never greys) via the child radio's VisibleWhen. No
    // defaultValue anywhere: a privacy opt-in carries no per-row reset.
    public IReadOnlyList<SettingDescriptor> Settings =>
    [
        Setting.Toggle("GeneralLatencyCard",
            () => TelemetryLatencyEnabled,
            value => TelemetryLatencyEnabled = value),
        Setting.Group("GeneralCorpusExpander",
            () => TelemetryCorpusEnabled,
            value => TelemetryCorpusEnabled = value,
            confirmOnEnable: TelemetryConsent.RequestCorpus,
            children:
            [
                Setting.Toggle("GeneralAudioCorpusCard",
                    () => RecordAudioCorpus,
                    value => RecordAudioCorpus = value,
                    confirmOnEnable: TelemetryConsent.RequestAudioCorpus),
                Setting.Radio("GeneralAudioCorpusContentCard",
                    () => AudioCorpusContentIndex,
                    value => AudioCorpusContentIndex = value,
                    options:
                    [
                        (0, "GeneralAudioCorpusContentMatch"),
                        (1, "GeneralAudioCorpusContentRaw"),
                    ],
                    visibleWhen: () => RecordAudioCorpus),
            ]),
    ];

    // Called by the Diagnostics page's Reset all: every dataset off. Outside
    // the sync guard, so each change pushes and saves like a user's flip. The
    // audio-content choice is not an opt-in and stays as the user set it.
    public void TurnOff()
    {
        TelemetryLatencyEnabled = false;
        RecordAudioCorpus = false;
        TelemetryCorpusEnabled = false;
    }

    // The module's entry in the Diagnostics registry.
    public static DiagnosticsContribution Contribution(int order) => new(
        "transcription", order,
        () =>
        {
            var model = new WhisperTelemetryViewModel();
            return new DiagnosticsSource(model, model.Settings, model.Load, model.TurnOff);
        });
}
