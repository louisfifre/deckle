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

    // ── Constructor ──────────────────────────────────────────────────────────

    public WhisperViewModel()
    {
        _isSyncing = true;

        ModelsDirectory = "";
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
