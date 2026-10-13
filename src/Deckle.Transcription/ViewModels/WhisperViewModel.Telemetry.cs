using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Transcription;

public partial class WhisperViewModel
{
    // Purpose-specific dataset consents stay beside the workflow that produces
    // them. Operational-detail admission is edited centrally on DiagnosticsPage.

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
        // RadioButtons emits -1 transiently while it realises its items.
        if (_isSyncing || value < 0) return;
        PushTelemetryToSettings();
    }

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

