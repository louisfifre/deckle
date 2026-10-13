using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Diagnostics.Telemetry;
using Deckle.Presets;
using Deckle.Settings;

namespace Deckle.Transcription;

// ViewModel for WhisperPage. Load() pulls from the settings POCOs, property
// changes push back via PushToSettings(); _isSyncing prevents re-saving during
// Load().
//
// NumberBox-bound properties are double (NumberBox.Value type). The NaN guards
// in the change handlers and PushToSettings prevent saving when the user clears
// a field.
//
// Partial properties (not fields) for WinRT/AOT compatibility (MVVMTK0045).
public partial class WhisperViewModel : ObservableObject
{
    private bool _isSyncing;

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

        // Curve seeds read from the segmenter POCO initializers (their single
        // source), overwritten by Load().
        var segmenter = new EnergySegmenterSettings();
        SegCurveX1 = segmenter.HangoverCurveX1;
        SegCurveY1 = segmenter.HangoverCurveY1;
        SegCurveX2 = segmenter.HangoverCurveX2;
        SegCurveY2 = segmenter.HangoverCurveY2;

        // A first family bound to a throwaway segmenter: the page composes the
        // selector before Load() runs and only needs the options, and nothing of
        // it may reach the settings file. Load() builds the real one.
        _cutPresets = BuildCutPresets(() => segmenter, () => { });
        CutPreset = _cutPresets.Selection;

        // Dictation-experience seeds from the shell POCO initializers (the single
        // source the shell's SettingsService persists), overwritten by Load().
        var overlay = new OverlaySettings();
        AutoPasteEnabled = new PasteSettings().AutoPasteEnabled;
        OverlayEnabled = overlay.Enabled;
        OverlayFadeOnProximity = overlay.FadeOnProximity;
        OverlayAnimations = overlay.Animations;
        OverlayPosition = (overlay.Position ?? "").StartsWith("Top") ? "TopCenter" : "BottomCenter";
        TranscriptPanelSide = NormalizeTranscriptPanelSide(overlay.TranscriptPanelSide);

        // Dataset opt-ins seed closed until the user consents. Overwritten by Load().
        TelemetryLatencyEnabled = false;
        TelemetryCorpusEnabled = false;
        RecordAudioCorpus = false;
        AudioCorpusContentIndex = 0;

        // _isSyncing stays true until Load(), so no setter saves the seeds.
    }

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
            SegCurveX1 = s.Streaming.Segmenter.HangoverCurveX1;
            SegCurveY1 = s.Streaming.Segmenter.HangoverCurveY1;
            SegCurveX2 = s.Streaming.Segmenter.HangoverCurveX2;
            SegCurveY2 = s.Streaming.Segmenter.HangoverCurveY2;
            SegMarginMs = s.Streaming.Segmenter.MarginMs;
            SegMinUtteranceMs = s.Streaming.Segmenter.MinUtteranceMs;

            // The shell's SettingsService persists these, not TranscriptionSettings.
            var shell = SettingsService.Instance.Current;
            AutoPasteEnabled = shell.Paste.AutoPasteEnabled;
            OverlayEnabled = shell.Overlay.Enabled;
            OverlayFadeOnProximity = shell.Overlay.FadeOnProximity;
            OverlayAnimations = shell.Overlay.Animations;
            OverlayPosition = (shell.Overlay.Position ?? "").StartsWith("Top") ? "TopCenter" : "BottomCenter";
            TranscriptPanelSide = NormalizeTranscriptPanelSide(shell.Overlay.TranscriptPanelSide);

            var t = TelemetrySettingsService.Instance.Current;
            TelemetryLatencyEnabled = t.LatencyEnabled;
            TelemetryCorpusEnabled = t.CorpusEnabled;
            RecordAudioCorpus = t.RecordAudioCorpus;
            AudioCorpusContentIndex = (int)t.AudioCorpusContent;

            // Last: when the file holds no selection, the real family derives one
            // from the hangover values read above and stores it.
            _cutPresets = BuildCutPresets(
                () => TranscriptionSettingsService.Instance.Current.Streaming.Segmenter,
                TranscriptionSettingsService.Instance.Save);
            CutPreset = _cutPresets.Selection;
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
        if (!double.IsNaN(SegCurveX1))             s.Streaming.Segmenter.HangoverCurveX1     = SegCurveX1;
        if (!double.IsNaN(SegCurveY1))             s.Streaming.Segmenter.HangoverCurveY1     = SegCurveY1;
        if (!double.IsNaN(SegCurveX2))             s.Streaming.Segmenter.HangoverCurveX2     = SegCurveX2;
        if (!double.IsNaN(SegCurveY2))             s.Streaming.Segmenter.HangoverCurveY2     = SegCurveY2;
        if (!double.IsNaN(SegMarginMs))           s.Streaming.Segmenter.MarginMs            = (int)SegMarginMs;
        if (!double.IsNaN(SegMinUtteranceMs))      s.Streaming.Segmenter.MinUtteranceMs      = (int)SegMinUtteranceMs;

        TranscriptionSettingsService.Instance.Save();
    }
}
