using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Diagnostics.Telemetry;
using Deckle.Settings;

namespace Deckle.Transcription;

// ViewModel for WhisperPage — bridges the 7 AppSettings sections
// (Paths, Transcription, SpeechDetection, Decoding, Confidence,
// OutputFilters, Context) to the XAML via x:Bind.
//
// Pattern: Load() pulls from the POCO, property changes push back via
// PushToSettings(). The _isSyncing flag prevents re-saving during Load().
//
// Model and Language are set from code-behind (combo handlers), not
// bound in XAML — same pattern as GeneralViewModel.AudioInputDeviceId.
//
// NumberBox-bound properties are double (NumberBox.Value type). NaN guard
// in OnXChanged and PushToSettings prevents saving when the user clears
// a field.
//
// Partial properties (not fields) for WinRT/AOT compatibility (MVVMTK0045).
public partial class WhisperViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Paths ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string ModelsDirectory { get; set; }

    partial void OnModelsDirectoryChanged(string value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    [ObservableProperty]
    public partial string FileTranscriptionOutputDirectory { get; set; }

    partial void OnFileTranscriptionOutputDirectoryChanged(string value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    // ── Transcription ────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string Model { get; set; }

    [ObservableProperty]
    public partial bool UseGpu { get; set; }

    [ObservableProperty]
    public partial string Language { get; set; }

    [ObservableProperty]
    public partial string InitialPrompt { get; set; }

    partial void OnModelChanged(string value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnUseGpuChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnLanguageChanged(string value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnInitialPromptChanged(string value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    // ── Voice activity detection ─────────────────────────────────────────────

    // The "Voice activity detection" toggle and its four Silero detection
    // parameters. All drive the external Silero ONNX VAD (Streaming.SpeechTrim) —
    // the whisper-internal VAD they used to bind is unplugged. The parameters are
    // double for the Slider; cast to the POCO's float/int on push.
    [ObservableProperty]
    public partial bool VadEnabled { get; set; }

    [ObservableProperty]
    public partial double VadThreshold { get; set; }

    [ObservableProperty]
    public partial double VadMinSpeechDurationMs { get; set; }

    [ObservableProperty]
    public partial double VadMinSilenceDurationMs { get; set; }

    [ObservableProperty]
    public partial double VadSpeechPadMs { get; set; }

    partial void OnVadEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnVadThresholdChanged(double value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnVadMinSpeechDurationMsChanged(double value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnVadMinSilenceDurationMsChanged(double value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnVadSpeechPadMsChanged(double value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    // ── Decoding ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool UseBeamSearch { get; set; }

    [ObservableProperty]
    public partial double BeamSize { get; set; }

    [ObservableProperty]
    public partial double Temperature { get; set; }

    [ObservableProperty]
    public partial double TemperatureIncrement { get; set; }

    partial void OnUseBeamSearchChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnBeamSizeChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnTemperatureChanged(double value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnTemperatureIncrementChanged(double value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    // ── Confidence ───────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial double EntropyThreshold { get; set; }

    [ObservableProperty]
    public partial double LogprobThreshold { get; set; }

    [ObservableProperty]
    public partial double NoSpeechThreshold { get; set; }

    partial void OnEntropyThresholdChanged(double value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnLogprobThresholdChanged(double value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnNoSpeechThresholdChanged(double value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    // ── Output Filters ───────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool SuppressNonSpeechTokens { get; set; }

    [ObservableProperty]
    public partial bool SuppressBlank { get; set; }

    [ObservableProperty]
    public partial string SuppressRegex { get; set; }

    partial void OnSuppressNonSpeechTokensChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnSuppressBlankChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnSuppressRegexChanged(string value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    // ── Context ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool UseContext { get; set; }

    [ObservableProperty]
    public partial double MaxTokens { get; set; }

    partial void OnUseContextChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnMaxTokensChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    // ── Streaming pipeline ───────────────────────────────────────────────────
    //
    // StreamingEnabled is the user-facing on/off mapped onto the two-value
    // PipelineStrategyKind (Streaming / Monolithic). The Seg* values are the
    // energy-segmenter parameters, consulted only when streaming is on. The
    // hangover is dynamic: HangoverMax at the start of an utterance, decaying
    // along the configured Bézier curve to HangoverMin between RampStart and
    // RampEnd lengths.

    [ObservableProperty]
    public partial bool StreamingEnabled { get; set; }

    [ObservableProperty]
    public partial double SegThresholdDbfs { get; set; }

    [ObservableProperty]
    public partial double SegHangoverMaxMs { get; set; }

    [ObservableProperty]
    public partial double SegHangoverMinMs { get; set; }

    [ObservableProperty]
    public partial double SegHangoverRampStartMs { get; set; }

    [ObservableProperty]
    public partial double SegHangoverRampEndMs { get; set; }

    [ObservableProperty]
    public partial double SegMarginMs { get; set; }

    [ObservableProperty]
    public partial double SegMinUtteranceMs { get; set; }

    partial void OnStreamingEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushToSettings();
    }

    partial void OnSegThresholdDbfsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegHangoverMaxMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegHangoverMinMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegHangoverRampStartMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegHangoverRampEndMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegMarginMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegMinUtteranceMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    // ── Dictation experience (overlay HUD + auto-paste) ──────────────────────
    //
    // Relocated from GeneralPage in the settings reorg: the on-screen HUD shown
    // during dictation (master toggle + fade-on-proximity, animations, position)
    // and whether the transcript is pasted into the focused window after copy.
    // These describe how dictation surfaces itself and delivers its output, so
    // they live on the Dictation page beside the engine that produces them.
    //
    // Persistence stays in the shell's Overlay / Paste sections (settings.json),
    // read at runtime by the HUD (Deckle.Hud) and the hotkey paste path
    // (App.Hotkeys) — this VM only surfaces them. Pushed through a dedicated
    // PushBehaviourToSettings so the shell save stays separate from the module's
    // TranscriptionSettings save.

    [ObservableProperty]
    public partial bool AutoPasteEnabled { get; set; }

    [ObservableProperty]
    public partial bool OverlayEnabled { get; set; }

    [ObservableProperty]
    public partial bool OverlayFadeOnProximity { get; set; }

    [ObservableProperty]
    public partial bool OverlayAnimations { get; set; }

    [ObservableProperty]
    public partial string OverlayPosition { get; set; }

    partial void OnAutoPasteEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    partial void OnOverlayEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    partial void OnOverlayFadeOnProximityChanged(bool value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    partial void OnOverlayAnimationsChanged(bool value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    partial void OnOverlayPositionChanged(string value)
    {
        if (_isSyncing) return;
        PushBehaviourToSettings();
    }

    // Writes the overlay/paste values back to the shell's Overlay/Paste sections —
    // kept separate from PushToSettings (which persists this module's own
    // TranscriptionSettings) because these live in the shell's settings.json.
    private void PushBehaviourToSettings()
    {
        var shell = SettingsService.Instance.Current;
        shell.Paste.AutoPasteEnabled = AutoPasteEnabled;
        shell.Overlay.Enabled = OverlayEnabled;
        shell.Overlay.FadeOnProximity = OverlayFadeOnProximity;
        shell.Overlay.Animations = OverlayAnimations;
        shell.Overlay.Position = OverlayPosition;
        SettingsService.Instance.Save();
    }

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

    // ── Constructor ──────────────────────────────────────────────────────────

    public WhisperViewModel()
    {
        _isSyncing = true;

        ModelsDirectory = "";
        FileTranscriptionOutputDirectory = "";
        Model = "ggml-base.bin";
        UseGpu = true;
        Language = "fr";
        InitialPrompt = "Bonjour. Voici une transcription en français, avec une ponctuation soignée et des phrases complètes.";
        VadEnabled = true;
        VadThreshold = 0.5;
        VadMinSpeechDurationMs = 250;
        VadMinSilenceDurationMs = 100;
        VadSpeechPadMs = 30;
        UseBeamSearch = true;
        BeamSize = 5;
        Temperature = 0.0;
        TemperatureIncrement = 0.2;
        EntropyThreshold = 2.4;
        LogprobThreshold = -1.0;
        NoSpeechThreshold = 0.6;
        SuppressNonSpeechTokens = true;
        SuppressBlank = true;
        SuppressRegex = "";
        UseContext = true;
        MaxTokens = -1;
        StreamingEnabled = false;
        SegThresholdDbfs = -45.0;
        SegHangoverMaxMs = 5_000;
        SegHangoverMinMs = 500;
        SegHangoverRampStartMs = 15_000;
        SegHangoverRampEndMs = 120_000;
        SegMarginMs = 150;
        SegMinUtteranceMs = 250;

        // Dictation-experience seeds from the shell POCO initializers (the single
        // source the shell's SettingsService persists), overwritten by Load().
        var overlay = new OverlaySettings();
        AutoPasteEnabled = new PasteSettings().AutoPasteEnabled;
        OverlayEnabled = overlay.Enabled;
        OverlayFadeOnProximity = overlay.FadeOnProximity;
        OverlayAnimations = overlay.Animations;
        OverlayPosition = (overlay.Position ?? "").StartsWith("Top") ? "TopCenter" : "BottomCenter";

        // Dataset opt-ins seed closed until the user consents. Overwritten by Load().
        TelemetryLatencyEnabled = false;
        TelemetryCorpusEnabled = false;
        RecordAudioCorpus = false;
        AudioCorpusContentIndex = 0;

        // _isSyncing stays true — Load() will set it to false.
    }

    // ── Sync with SettingsService ────────────────────────────────────────────

    // All transcription settings live in modules/transcription/settings.json
    // and ModelsDirectory moved off Paths into TranscriptionSettings — so a single
    // module read covers everything this VM exposes.
    public void Load()
    {
        _isSyncing = true;
        try
        {
            var s = TranscriptionSettingsService.Instance.Current;
            ModelsDirectory = s.ModelsDirectory;
            FileTranscriptionOutputDirectory = s.FileTranscriptionOutputDirectory;
            Model = s.Engine.Model;
            UseGpu = s.Engine.UseGpu;
            Language = s.Engine.Language;
            InitialPrompt = s.Engine.InitialPrompt;
            VadEnabled = s.Streaming.SpeechTrim.Enabled;
            VadThreshold = s.Streaming.SpeechTrim.Threshold;
            VadMinSpeechDurationMs = s.Streaming.SpeechTrim.MinSpeechDurationMs;
            VadMinSilenceDurationMs = s.Streaming.SpeechTrim.MinSilenceDurationMs;
            VadSpeechPadMs = s.Streaming.SpeechTrim.SpeechPadMs;
            UseBeamSearch = s.Decoding.UseBeamSearch;
            BeamSize = s.Decoding.BeamSize;
            Temperature = s.Decoding.Temperature;
            TemperatureIncrement = s.Decoding.TemperatureIncrement;
            EntropyThreshold = s.Confidence.EntropyThreshold;
            LogprobThreshold = s.Confidence.LogprobThreshold;
            NoSpeechThreshold = s.Confidence.NoSpeechThreshold;
            SuppressNonSpeechTokens = s.OutputFilters.SuppressNonSpeechTokens;
            SuppressBlank = s.OutputFilters.SuppressBlank;
            SuppressRegex = s.OutputFilters.SuppressRegex;
            UseContext = s.Context.UseContext;
            MaxTokens = s.Context.MaxTokens;
            StreamingEnabled = s.Streaming.Strategy == PipelineStrategyKind.Streaming;
            SegThresholdDbfs = s.Streaming.Segmenter.ThresholdDbfs;
            SegHangoverMaxMs = s.Streaming.Segmenter.HangoverMaxMs;
            SegHangoverMinMs = s.Streaming.Segmenter.HangoverMinMs;
            SegHangoverRampStartMs = s.Streaming.Segmenter.HangoverRampStartMs;
            SegHangoverRampEndMs = s.Streaming.Segmenter.HangoverRampEndMs;
            SegMarginMs = s.Streaming.Segmenter.MarginMs;
            SegMinUtteranceMs = s.Streaming.Segmenter.MinUtteranceMs;

            // Dictation experience — read from the shell's Overlay/Paste sections
            // (persisted separately from this module's TranscriptionSettings).
            var shell = SettingsService.Instance.Current;
            AutoPasteEnabled = shell.Paste.AutoPasteEnabled;
            OverlayEnabled = shell.Overlay.Enabled;
            OverlayFadeOnProximity = shell.Overlay.FadeOnProximity;
            OverlayAnimations = shell.Overlay.Animations;
            OverlayPosition = (shell.Overlay.Position ?? "").StartsWith("Top") ? "TopCenter" : "BottomCenter";

            // Dataset consents are read here so composed cards reflect persisted state.
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

    private void PushToSettings()
    {
        var s = TranscriptionSettingsService.Instance.Current;

        s.ModelsDirectory = ModelsDirectory;
        s.FileTranscriptionOutputDirectory = FileTranscriptionOutputDirectory;
        s.Engine.Model = Model;
        s.Engine.UseGpu = UseGpu;
        s.Engine.Language = Language;
        s.Engine.InitialPrompt = InitialPrompt;

        s.Streaming.SpeechTrim.Enabled = VadEnabled;
        s.Streaming.SpeechTrim.Threshold = (float)VadThreshold;
        s.Streaming.SpeechTrim.MinSpeechDurationMs = (int)VadMinSpeechDurationMs;
        s.Streaming.SpeechTrim.MinSilenceDurationMs = (int)VadMinSilenceDurationMs;
        s.Streaming.SpeechTrim.SpeechPadMs = (int)VadSpeechPadMs;

        s.Decoding.Temperature = Temperature;
        s.Decoding.TemperatureIncrement = TemperatureIncrement;
        s.Decoding.UseBeamSearch = UseBeamSearch;
        if (!double.IsNaN(BeamSize))
            s.Decoding.BeamSize = (int)BeamSize;

        s.Confidence.EntropyThreshold = EntropyThreshold;
        s.Confidence.LogprobThreshold = LogprobThreshold;
        s.Confidence.NoSpeechThreshold = NoSpeechThreshold;

        s.OutputFilters.SuppressNonSpeechTokens = SuppressNonSpeechTokens;
        s.OutputFilters.SuppressBlank = SuppressBlank;
        s.OutputFilters.SuppressRegex = SuppressRegex;

        s.Context.UseContext = UseContext;
        if (!double.IsNaN(MaxTokens))
            s.Context.MaxTokens = (int)MaxTokens;

        s.Streaming.Strategy = StreamingEnabled
            ? PipelineStrategyKind.Streaming
            : PipelineStrategyKind.Monolithic;
        s.Streaming.Segmenter.ThresholdDbfs = SegThresholdDbfs;
        if (!double.IsNaN(SegHangoverMaxMs))       s.Streaming.Segmenter.HangoverMaxMs       = (int)SegHangoverMaxMs;
        if (!double.IsNaN(SegHangoverMinMs))       s.Streaming.Segmenter.HangoverMinMs       = (int)SegHangoverMinMs;
        if (!double.IsNaN(SegHangoverRampStartMs)) s.Streaming.Segmenter.HangoverRampStartMs = (int)SegHangoverRampStartMs;
        if (!double.IsNaN(SegHangoverRampEndMs))   s.Streaming.Segmenter.HangoverRampEndMs   = (int)SegHangoverRampEndMs;
        if (!double.IsNaN(SegMarginMs))            s.Streaming.Segmenter.MarginMs            = (int)SegMarginMs;
        if (!double.IsNaN(SegMinUtteranceMs))      s.Streaming.Segmenter.MinUtteranceMs      = (int)SegMinUtteranceMs;

        TranscriptionSettingsService.Instance.Save();
    }
}
