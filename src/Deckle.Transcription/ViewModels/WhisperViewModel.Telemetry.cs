using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Transcription;

public partial class WhisperViewModel
{
    // ── Observability (dictation logging + telemetry opt-ins) ────────────────
    //
    // Purpose-specific dataset consents stay beside the workflow that produces
    // them. Operational-detail admission is edited centrally on DiagnosticsPage.

    [ObservableProperty]
    public partial bool TelemetryLatencyEnabled { get; set; }

    [ObservableProperty]
    public partial bool TelemetryCorpusEnabled { get; set; }

    [ObservableProperty]
    public partial bool RecordAudioCorpus { get; set; }

    // Audio-corpus content selector — int index mirroring the RadioButtons order
    // (0 = match the transcription, 1 = always raw), mapped to
    // TelemetrySettings.AudioCorpusContent in Load / Push. Same idiom as
    // DiagnosticsViewModel: RadioButtons emits -1 transiently while it realises its
    // items, so the handler and the push both guard against a negative index.
    [ObservableProperty]
    public partial int AudioCorpusContentIndex { get; set; }

    partial void OnTelemetryLatencyEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushTelemetryToSettings();
    }

    partial void OnTelemetryCorpusEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushTelemetryToSettings();
    }

    partial void OnRecordAudioCorpusChanged(bool value)
    {
        if (_isSyncing) return;
        PushTelemetryToSettings();
    }

    partial void OnAudioCorpusContentIndexChanged(int value)
    {
        // RadioButtons emits -1 transiently while it realises its items —
        // ignore it so we never cast a bogus index onto the enum.
        if (_isSyncing || value < 0) return;
        PushTelemetryToSettings();
    }

    // Writes the four dictation telemetry opt-ins back to TelemetrySettings. The
    // index↔enum guard mirrors DiagnosticsViewModel: a transient negative index
    // resolves to MatchTranscription rather than casting a bogus value onto the enum.
    private void PushTelemetryToSettings()
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
}

