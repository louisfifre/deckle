using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Transcription;

public partial class WhisperViewModel
{
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
}

