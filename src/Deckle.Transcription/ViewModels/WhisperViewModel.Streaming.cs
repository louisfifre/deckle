using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Presets;

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

    // The two Bezier control points shaping the decay between RampStart and
    // RampEnd (EnergySegmenterSettings.HangoverCurveX1..Y2), each in [0, 1].
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
        RefreshCutPreset();
    }

    partial void OnSegHangoverMinMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        RefreshCutPreset();
    }

    partial void OnSegHangoverRampStartMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        RefreshCutPreset();
    }

    partial void OnSegHangoverRampEndMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        RefreshCutPreset();
    }

    partial void OnSegCurveX1Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        RefreshCutPreset();
    }

    partial void OnSegCurveY1Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        RefreshCutPreset();
    }

    partial void OnSegCurveX2Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        RefreshCutPreset();
    }

    partial void OnSegCurveY2Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        RefreshCutPreset();
    }

    // ── Cut preset ───────────────────────────────────────────────────────────
    //
    // CutPreset is what the selector shows: a preset id when the eight hangover
    // values match one, PresetFamily.Custom otherwise. Picking Custom changes no
    // value (it only reveals the fine controls). Picking a preset writes the
    // eight values through the normal setters (each pushes to the store), under
    // _applyingPreset so the per-value hooks do not recompute the selection
    // mid-write. The persisted truth stays the values; Custom is never stored.

    private readonly PresetFamily<CutCurveValues> _cutPresets;
    private bool _applyingPreset;

    [ObservableProperty]
    public partial string CutPreset { get; set; }

    partial void OnCutPresetChanged(string value)
    {
        if (_isSyncing || value == PresetFamily.Custom) return;
        _applyingPreset = true;
        try
        {
            _cutPresets.Apply(value);
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    internal CutCurveValues ReadCutValues() => new(
        (int)SegHangoverMaxMs,
        (int)SegHangoverMinMs,
        (int)SegHangoverRampStartMs,
        (int)SegHangoverRampEndMs,
        SegCurveX1,
        SegCurveY1,
        SegCurveX2,
        SegCurveY2);

    internal void WriteCutValues(CutCurveValues v)
    {
        SegHangoverMaxMs = v.HangoverMaxMs;
        SegHangoverMinMs = v.HangoverMinMs;
        SegHangoverRampStartMs = v.HangoverRampStartMs;
        SegHangoverRampEndMs = v.HangoverRampEndMs;
        SegCurveX1 = v.CurveX1;
        SegCurveY1 = v.CurveY1;
        SegCurveX2 = v.CurveX2;
        SegCurveY2 = v.CurveY2;
    }

    private void RefreshCutPreset()
    {
        if (_isSyncing || _applyingPreset) return;
        CutPreset = _cutPresets.Selection;
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

