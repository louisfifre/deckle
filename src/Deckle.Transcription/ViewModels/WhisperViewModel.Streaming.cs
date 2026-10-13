using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Presets;

namespace Deckle.Transcription;

public partial class WhisperViewModel
{
    // The Vad* values drive the Silero ONNX trim (Streaming.SpeechTrim), not
    // whisper.cpp's own VAD.
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

    // The Seg* values tune the energy segmenter, consulted only when streaming is
    // on.

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
        NotifyCutValueEdited();
    }

    partial void OnSegHangoverMinMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        NotifyCutValueEdited();
    }

    partial void OnSegHangoverRampStartMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        NotifyCutValueEdited();
    }

    partial void OnSegHangoverRampEndMsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        NotifyCutValueEdited();
    }

    partial void OnSegCurveX1Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        NotifyCutValueEdited();
    }

    partial void OnSegCurveY1Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        NotifyCutValueEdited();
    }

    partial void OnSegCurveX2Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        NotifyCutValueEdited();
    }

    partial void OnSegCurveY2Changed(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        PushToSettings();
        NotifyCutValueEdited();
    }

    private PresetFamily<CutCurveValues> _cutPresets;

    // Mirrors the family's stored selection: a preset id or PresetFamily.Custom.
    [ObservableProperty]
    public partial string CutPreset { get; set; }

    partial void OnCutPresetChanged(string value)
    {
        if (_isSyncing) return;
        _cutPresets.Select(value);
    }

    private PresetFamily<CutCurveValues> BuildCutPresets(Func<EnergySegmenterSettings> segmenter, Action save) =>
        new(CutPresets.All, CutPresets.Default, new CutPresetStore(this, segmenter, save));

    // Skipped while a preset is being applied: the store has not written the new
    // selection yet, and mirroring would put the old one back on the selector.
    // _isSyncing keeps OnCutPresetChanged from calling Select again for a change
    // that came from the family.
    private void NotifyCutValueEdited()
    {
        if (_isSyncing || _cutPresets.IsApplying) return;
        _cutPresets.NotifyEdited();

        _isSyncing = true;
        try
        {
            CutPreset = _cutPresets.Selection;
        }
        finally
        {
            _isSyncing = false;
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

