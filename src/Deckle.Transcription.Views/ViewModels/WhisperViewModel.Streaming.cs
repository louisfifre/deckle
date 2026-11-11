using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Transcription.Views;

public partial class WhisperViewModel
{
    // The Vad* values drive the Silero ONNX trim (Streaming.SpeechTrim), not
    // whisper.cpp's own VAD.
    [ObservableProperty]
    public partial bool VadEnabled { get; set; }

    [ObservableProperty]
    public partial double VadThreshold { get; set; }

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

    // The Seg* values tune the energy segmenter, which cuts every recording:
    // streaming dictation, files and recovered takes.

    [ObservableProperty]
    public partial bool StreamingEnabled { get; set; }

    [ObservableProperty]
    public partial double SegHangoverMaxMs { get; set; }

    [ObservableProperty]
    public partial double SegHangoverMinMs { get; set; }

    [ObservableProperty]
    public partial double SegHangoverRampStartMs { get; set; }

    [ObservableProperty]
    public partial double SegHangoverRampEndMs { get; set; }

    // Control points of the hangover decay curve, each in [0, 1].
    [ObservableProperty]
    public partial double SegCurveX1 { get; set; }

    [ObservableProperty]
    public partial double SegCurveY1 { get; set; }

    [ObservableProperty]
    public partial double SegCurveX2 { get; set; }

    [ObservableProperty]
    public partial double SegCurveY2 { get; set; }

    [ObservableProperty]
    public partial double SegMarginMs { get; set; }

    // The shared minimum speech (Streaming.MinSpeechMs): the segmenter's floor
    // on an utterance's voiced extent and Silero's on each span inside it.
    [ObservableProperty]
    public partial double MinSpeechMs { get; set; }

    partial void OnStreamingEnabledChanged(bool value)
    {
        if (_isSyncing) return;
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

    partial void OnSegCurveX1Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegCurveY1Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegCurveX2Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegCurveY2Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnSegMarginMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }

    partial void OnMinSpeechMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
    }
}

