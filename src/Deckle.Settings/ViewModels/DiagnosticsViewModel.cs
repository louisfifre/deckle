using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Settings;

// ViewModel for DiagnosticsPage — bridges TelemetrySettings and
// LoggingSettings to the XAML via x:Bind. Originally migrated from
// GeneralViewModel in slice S2 (Telemetry only) ; J4 polish added the
// Logging section to host runtime emission filters orthogonal to
// disk persistence, which expanded the VM to cover two stores.
//
// Pattern : Load() pulls from each store, property changes push back
// via the matching PushXxxToSettings(). The _isSyncing flag prevents
// re-saving during Load(). The split between PushLoggingToSettings()
// and PushTelemetryToSettings() lets a single toggle touch only its
// own store — flipping Verbose logging doesn't rewrite the telemetry
// JSON file, which matters because the two share neither schema nor
// lifecycle.
public partial class DiagnosticsViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Logging — runtime emission filters ──────────────────────────────────

    // Capture-window logging toggle for the ambient pipeline. The
    // listener-side filter (App.ShouldDropAmbientCaptureVerbose, combining
    // AmbientCaptureGate with this toggle) decides, per Verbose event from
    // the ambient providers — Deckle.Ambient / Vision / Lighting and the
    // per-frame Deckle.Diagnostics.Resource firehose — what reaches the
    // LogWindow buffer or app.jsonl. Off (default): nothing Verbose passes
    // while a capture loop runs, the 5 s heartbeat included — the user
    // only sees the Info / Warning / Error milestones that say whether it
    // runs and when it stops. On: the 5 s heartbeat and the occasional
    // start / stop detail pass, but the high-frequency firehose (per-tick
    // Push, per-frame Resource) stays dropped even opt-in — the rollup is
    // the ceiling, the per-event torrent is reserved for a deep dive in
    // code. The gate is opened right before the push loop (after the
    // started milestones) and closed at the top of Stop (before the
    // stopped milestones), so the bracketing milestones and any
    // out-of-loop user action pass through. Non-Verbose levels always
    // pass. The section will grow with sibling per-loop toggles for
    // Whisp / Audio / Llm. Wired through LoggingSettingsService — separate
    // store from TelemetrySettings so flipping it leaves the disk-
    // persistence opt-ins untouched.
    [ObservableProperty]
    public partial bool LogAmbientCaptureActivity { get; set; }

    // Streaming transcription Verbose toggle: when off, the 1 Hz heartbeat and
    // the per-utterance details from the Whisp provider are dropped during a
    // streaming take. Milestones (StreamingPipelineStarted, StreamingDrained)
    // always pass. Sister to LogAmbientCaptureActivity; same closed-by-default
    // posture.
    [ObservableProperty]
    public partial bool LogStreamingTranscriptionActivity { get; set; }

    // Autocorrect Verbose toggle: when off (default), only the edits surface —
    // an applied correction's Verbose detail (reason and lengths, never the
    // word) plus its milestone. The per-focus SurfaceChanged firehose, the
    // learning signals and the 30 s activity rollup are dropped; a heartbeat is
    // meaningless for a keystroke-driven subsystem. Sister to
    // LogAmbientCaptureActivity, but with no capture window — the engine runs
    // continuously.
    [ObservableProperty]
    public partial bool LogAutocorrectActivity { get; set; }

    // Windowing Verbose toggle: when off (default), the whole Deckle-Windowing
    // firehose is dropped — placement, overlay slots, popup anchoring, z-order,
    // resize frames, first-open timings. The provider emits Verbose only, so off
    // means a fully silent channel; on surfaces everything for a placement /
    // resize-lag dive. Sister to LogAutocorrectActivity, no capture window.
    [ObservableProperty]
    public partial bool LogWindowingActivity { get; set; }

    // ── Telemetry — opt-in disk persistence ─────────────────────────────────

    // Application log — mirrors every in-app log line to app.jsonl. Top of
    // section by user request : the most asked-for diagnostic when
    // troubleshooting an issue across restarts.
    [ObservableProperty]
    public partial bool ApplicationLogToDisk { get; set; }

    // Microphone telemetry — when on, every Recording Stop logs an extra
    // line summarising the per-recording RMS distribution AND writes a
    // structured row to <telemetry>/microphone.jsonl. Calibration tool.
    [ObservableProperty]
    public partial bool MicrophoneTelemetry { get; set; }

    // Latency telemetry — per-step timings of each transcription written
    // to latency.jsonl. Timings only, no transcript text — lighter privacy
    // posture than Application log or Corpus.
    [ObservableProperty]
    public partial bool TelemetryLatencyEnabled { get; set; }

    // Corpus master — text corpus (transcription + rewrite) per profile.
    // Audio corpus is nested under it (gated by IsEnabled in XAML so it
    // can't reach the on state while the master is off).
    [ObservableProperty]
    public partial bool TelemetryCorpusEnabled { get; set; }

    [ObservableProperty]
    public partial bool RecordAudioCorpus { get; set; }

    // Audio corpus content selector — which version of each take is saved
    // when RecordAudioCorpus is on. Stored as an int index mirroring the
    // RadioButtons order (0 = match the transcription, 1 = always raw) so
    // RadioButtons.SelectedIndex binds TwoWay without a converter. Mapped
    // to TelemetrySettings.AudioCorpusContent in Load / Push.
    [ObservableProperty]
    public partial int AudioCorpusContentIndex { get; set; }

    // Autocorrect decisions — the per-word decision dataset
    // (autocorrect.decisions.jsonl): every corrected or left-literal word on an
    // enrolled surface with its candidates, scores, margins and the guard that
    // decided it. Carries typed words by design — the diagnostic surface for tuning
    // the corrector before adding grammar — so it is consent-gated like the other
    // text captures. Off by default.
    [ObservableProperty]
    public partial bool AutocorrectDecisions { get; set; }

    // Autocorrect text — the typed-sentence corpus (autocorrect.text.jsonl): each
    // sentence typed at the keyboard on an enrolled surface, verbatim form paired
    // with the corrected one. The substrate for modelling the user's own error
    // patterns. The heaviest text capture — a verbatim record of typed input — so it
    // is nested under the decision toggle in the UI and off by default.
    [ObservableProperty]
    public partial bool AutocorrectText { get; set; }

    // Storage folder override — empty = AppPaths.TelemetryDirectory.
    // FolderPickerCard.DefaultPath is wired to the resolved default in
    // the page code-behind ; the picker shows it as a placeholder when
    // the override is empty.
    [ObservableProperty]
    public partial string TelemetryStorageDirectory { get; set; }

    partial void OnLogAmbientCaptureActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogAmbientCaptureActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnLogStreamingTranscriptionActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogStreamingTranscriptionActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnLogAutocorrectActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogAutocorrectActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnLogWindowingActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogWindowingActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnApplicationLogToDiskChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.ApplicationLogToDisk", value.ToString());
        PushTelemetryToSettings();
    }

    partial void OnMicrophoneTelemetryChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.MicrophoneTelemetry", value.ToString());
        PushTelemetryToSettings();
    }

    partial void OnTelemetryLatencyEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.LatencyEnabled", value.ToString());
        PushTelemetryToSettings();
    }

    partial void OnTelemetryCorpusEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.CorpusEnabled", value.ToString());
        PushTelemetryToSettings();
    }

    partial void OnRecordAudioCorpusChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.RecordAudioCorpus", value.ToString());
        PushTelemetryToSettings();
    }

    partial void OnAudioCorpusContentIndexChanged(int value)
    {
        // RadioButtons emits -1 transiently while it realises its items —
        // ignore it so we never cast a bogus index onto the enum.
        if (_isSyncing || value < 0) return;
        DeckleSettingsUxSource.Log.SettingChanged(
            "Telemetry.AudioCorpusContent", ((AudioCorpusContent)value).ToString());
        PushTelemetryToSettings();
    }

    partial void OnAutocorrectDecisionsChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.AutocorrectDecisions", value.ToString());
        PushTelemetryToSettings();
    }

    partial void OnAutocorrectTextChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.AutocorrectText", value.ToString());
        PushTelemetryToSettings();
    }

    partial void OnTelemetryStorageDirectoryChanged(string value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.StorageDirectory", $"\"{value}\"");
        PushTelemetryToSettings();
    }

    // ── Sync with LoggingSettingsService and TelemetrySettingsService ───────

    public DiagnosticsViewModel()
    {
        // Guard BEFORE any property assignment — same reason as GeneralViewModel.
        _isSyncing = true;

        // Logging defaults are "closed" by family : every per-loop
        // capture toggle starts OFF because the routine cadence
        // drowns out everything else. Non-Verbose levels and
        // out-of-loop emissions are unaffected, so milestones,
        // errors, and user actions stay visible — only the per-tick
        // noise is suppressed. Telemetry defaults are also "closed"
        // but for a different reason : disk-persistence streams stay
        // off until the user explicitly opts in to where their data
        // lands.
        LogAmbientCaptureActivity = false;
        LogStreamingTranscriptionActivity = false;
        LogAutocorrectActivity = false;
        LogWindowingActivity = false;
        ApplicationLogToDisk = false;
        MicrophoneTelemetry = false;
        TelemetryLatencyEnabled = false;
        TelemetryCorpusEnabled = false;
        RecordAudioCorpus = false;
        AudioCorpusContentIndex = 0;
        AutocorrectDecisions = false;
        AutocorrectText = false;
        TelemetryStorageDirectory = "";

        // _isSyncing stays true — Load() will set it to false.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var l = LoggingSettingsService.Instance.Current;
            LogAmbientCaptureActivity = l.LogAmbientCaptureActivity;
            LogStreamingTranscriptionActivity = l.LogStreamingTranscriptionActivity;
            LogAutocorrectActivity = l.LogAutocorrectActivity;
            LogWindowingActivity = l.LogWindowingActivity;

            var t = TelemetrySettingsService.Instance.Current;
            ApplicationLogToDisk = t.ApplicationLogToDisk;
            MicrophoneTelemetry = t.MicrophoneTelemetry;
            TelemetryLatencyEnabled = t.LatencyEnabled;
            TelemetryCorpusEnabled = t.CorpusEnabled;
            RecordAudioCorpus = t.RecordAudioCorpus;
            AudioCorpusContentIndex = (int)t.AudioCorpusContent;
            AutocorrectDecisions = t.AutocorrectDecisions;
            AutocorrectText = t.AutocorrectText;
            TelemetryStorageDirectory = t.StorageDirectory;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void PushLoggingToSettings()
    {
        var l = LoggingSettingsService.Instance.Current;
        l.LogAmbientCaptureActivity = LogAmbientCaptureActivity;
        l.LogStreamingTranscriptionActivity = LogStreamingTranscriptionActivity;
        l.LogAutocorrectActivity = LogAutocorrectActivity;
        l.LogWindowingActivity = LogWindowingActivity;
        LoggingSettingsService.Instance.Save();
    }

    private void PushTelemetryToSettings()
    {
        var t = TelemetrySettingsService.Instance.Current;
        t.ApplicationLogToDisk = ApplicationLogToDisk;
        t.MicrophoneTelemetry = MicrophoneTelemetry;
        t.LatencyEnabled = TelemetryLatencyEnabled;
        t.CorpusEnabled = TelemetryCorpusEnabled;
        t.RecordAudioCorpus = RecordAudioCorpus;
        t.AudioCorpusContent = AudioCorpusContentIndex < 0
            ? AudioCorpusContent.MatchTranscription
            : (AudioCorpusContent)AudioCorpusContentIndex;
        t.AutocorrectDecisions = AutocorrectDecisions;
        t.AutocorrectText = AutocorrectText;
        t.StorageDirectory = TelemetryStorageDirectory ?? "";
        TelemetrySettingsService.Instance.Save();
    }

    // ── Reset ───────────────────────────────────────────────────────────────

    public void ResetLoggingDefaults()
    {
        _isSyncing = true;
        try
        {
            LogAmbientCaptureActivity = false;
            LogStreamingTranscriptionActivity = false;
            LogAutocorrectActivity = false;
            LogWindowingActivity = false;
        }
        finally { _isSyncing = false; }
        PushLoggingToSettings();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Logging");
    }

    public void ResetTelemetryDefaults()
    {
        _isSyncing = true;
        try
        {
            ApplicationLogToDisk = false;
            MicrophoneTelemetry = false;
            TelemetryLatencyEnabled = false;
            TelemetryCorpusEnabled = false;
            RecordAudioCorpus = false;
            AudioCorpusContentIndex = 0;
            AutocorrectDecisions = false;
            AutocorrectText = false;
            TelemetryStorageDirectory = "";
        }
        finally { _isSyncing = false; }
        PushTelemetryToSettings();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Telemetry");
    }
}
