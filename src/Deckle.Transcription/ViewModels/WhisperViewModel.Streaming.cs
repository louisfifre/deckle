using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Transcription;

public partial class WhisperViewModel
{
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
}

