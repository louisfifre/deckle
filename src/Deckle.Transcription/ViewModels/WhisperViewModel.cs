using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Transcription.ViewModels;

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
        DeckleWhispSource.Log.SettingChanged("Paths.ModelsDirectory", $"\"{value}\"");
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
        DeckleWhispSource.Log.SettingChanged("Transcription.Model", $"\"{value}\"");
        PushToSettings();
    }

    partial void OnUseGpuChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("Transcription.UseGpu", value.ToString());
        PushToSettings();
    }

    partial void OnLanguageChanged(string value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("Transcription.Language", $"\"{value}\"");
        PushToSettings();
    }

    partial void OnInitialPromptChanged(string value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("Transcription.InitialPrompt", $"({value?.Length ?? 0} chars)");
        PushToSettings();
    }

    // ── Speech Detection ─────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool VadEnabled { get; set; }

    [ObservableProperty]
    public partial double VadThreshold { get; set; }

    [ObservableProperty]
    public partial double VadMinSpeechDurationMs { get; set; }

    [ObservableProperty]
    public partial double VadMinSilenceDurationMs { get; set; }

    [ObservableProperty]
    public partial double VadMaxSpeechDurationSec { get; set; }

    [ObservableProperty]
    public partial double VadSpeechPadMs { get; set; }

    [ObservableProperty]
    public partial double VadSamplesOverlap { get; set; }

    partial void OnVadEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("SpeechDetection.Enabled", value.ToString());
        PushToSettings();
    }

    partial void OnVadThresholdChanged(double value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("SpeechDetection.Threshold", value.ToString("0.00"));
        PushToSettings();
    }

    partial void OnVadMinSpeechDurationMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("SpeechDetection.MinSpeechDurationMs", ((int)value).ToString());
        PushToSettings();
    }

    partial void OnVadMinSilenceDurationMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("SpeechDetection.MinSilenceDurationMs", ((int)value).ToString());
        PushToSettings();
    }

    partial void OnVadMaxSpeechDurationSecChanged(double value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("SpeechDetection.MaxSpeechDurationSec", ((int)value).ToString());
        PushToSettings();
    }

    partial void OnVadSpeechPadMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("SpeechDetection.SpeechPadMs", ((int)value).ToString());
        PushToSettings();
    }

    partial void OnVadSamplesOverlapChanged(double value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("SpeechDetection.SamplesOverlap", value.ToString("0.00"));
        PushToSettings();
    }

    // ── Decoding ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial double Temperature { get; set; }

    [ObservableProperty]
    public partial double TemperatureIncrement { get; set; }

    partial void OnTemperatureChanged(double value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("Decoding.Temperature", value.ToString("0.0"));
        PushToSettings();
    }

    partial void OnTemperatureIncrementChanged(double value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("Decoding.TemperatureIncrement", value.ToString("0.0"));
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
        DeckleWhispSource.Log.SettingChanged("Confidence.EntropyThreshold", value.ToString("0.0"));
        PushToSettings();
    }

    partial void OnLogprobThresholdChanged(double value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("Confidence.LogprobThreshold", value.ToString("0.00"));
        PushToSettings();
    }

    partial void OnNoSpeechThresholdChanged(double value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("Confidence.NoSpeechThreshold", value.ToString("0.00"));
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
        DeckleWhispSource.Log.SettingChanged("OutputFilters.SuppressNonSpeechTokens", value.ToString());
        PushToSettings();
    }

    partial void OnSuppressBlankChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("OutputFilters.SuppressBlank", value.ToString());
        PushToSettings();
    }

    partial void OnSuppressRegexChanged(string value)
    {
        if (_isSyncing) return;
        DeckleWhispSource.Log.SettingChanged("OutputFilters.SuppressRegex", $"\"{value}\"");
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
        DeckleWhispSource.Log.SettingChanged("Context.UseContext", value.ToString());
        PushToSettings();
    }

    partial void OnMaxTokensChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("Context.MaxTokens", ((int)value).ToString());
        PushToSettings();
    }

    // ── Streaming pipeline ───────────────────────────────────────────────────
    //
    // StreamingEnabled is the user-facing on/off mapped onto the two-value
    // PipelineStrategyKind (Streaming / Monolithic). The Seg* values are the
    // energy-segmenter parameters, consulted only when streaming is on. The
    // hangover is dynamic: HangoverMax at the start of an utterance, decaying
    // log-linearly to HangoverMin between RampStart and RampEnd lengths.

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
        DeckleWhispSource.Log.SettingChanged("Streaming.Strategy", value ? "Streaming" : "Monolithic");
        PushToSettings();
    }

    partial void OnSegThresholdDbfsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("Streaming.Segmenter.ThresholdDbfs", value.ToString("0.0"));
        PushToSettings();
    }

    partial void OnSegHangoverMaxMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("Streaming.Segmenter.HangoverMaxMs", ((int)value).ToString());
        PushToSettings();
    }

    partial void OnSegHangoverMinMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("Streaming.Segmenter.HangoverMinMs", ((int)value).ToString());
        PushToSettings();
    }

    partial void OnSegHangoverRampStartMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("Streaming.Segmenter.HangoverRampStartMs", ((int)value).ToString());
        PushToSettings();
    }

    partial void OnSegHangoverRampEndMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("Streaming.Segmenter.HangoverRampEndMs", ((int)value).ToString());
        PushToSettings();
    }

    partial void OnSegMarginMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("Streaming.Segmenter.MarginMs", ((int)value).ToString());
        PushToSettings();
    }

    partial void OnSegMinUtteranceMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleWhispSource.Log.SettingChanged("Streaming.Segmenter.MinUtteranceMs", ((int)value).ToString());
        PushToSettings();
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    public WhisperViewModel()
    {
        _isSyncing = true;

        ModelsDirectory = "";
        Model = "ggml-large-v3.bin";
        UseGpu = true;
        Language = "fr";
        InitialPrompt = "Bonjour. Voici une transcription en français, avec une ponctuation soignée et des phrases complètes.";
        VadEnabled = true;
        VadThreshold = 0.5;
        VadMinSpeechDurationMs = 250;
        VadMinSilenceDurationMs = 500;
        VadMaxSpeechDurationSec = 30.0;
        VadSpeechPadMs = 200;
        VadSamplesOverlap = 0.1;
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
        SegHangoverRampStartMs = 60_000;
        SegHangoverRampEndMs = 180_000;
        SegMarginMs = 150;
        SegMinUtteranceMs = 250;

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
            VadEnabled = s.SpeechDetection.Enabled;
            VadThreshold = s.SpeechDetection.Threshold;
            VadMinSpeechDurationMs = s.SpeechDetection.MinSpeechDurationMs;
            VadMinSilenceDurationMs = s.SpeechDetection.MinSilenceDurationMs;
            VadMaxSpeechDurationSec = s.SpeechDetection.MaxSpeechDurationSec;
            VadSpeechPadMs = s.SpeechDetection.SpeechPadMs;
            VadSamplesOverlap = s.SpeechDetection.SamplesOverlap;
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

        s.SpeechDetection.Enabled = VadEnabled;
        s.SpeechDetection.Threshold = (float)VadThreshold;
        if (!double.IsNaN(VadMinSpeechDurationMs))
            s.SpeechDetection.MinSpeechDurationMs = (int)VadMinSpeechDurationMs;
        if (!double.IsNaN(VadMinSilenceDurationMs))
            s.SpeechDetection.MinSilenceDurationMs = (int)VadMinSilenceDurationMs;
        s.SpeechDetection.MaxSpeechDurationSec = (float)VadMaxSpeechDurationSec;
        if (!double.IsNaN(VadSpeechPadMs))
            s.SpeechDetection.SpeechPadMs = (int)VadSpeechPadMs;
        s.SpeechDetection.SamplesOverlap = (float)VadSamplesOverlap;

        s.Decoding.Temperature = Temperature;
        s.Decoding.TemperatureIncrement = TemperatureIncrement;

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
