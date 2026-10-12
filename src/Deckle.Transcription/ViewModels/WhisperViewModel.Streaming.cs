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

    // ── Cut preset ───────────────────────────────────────────────────────────
    //
    // CutPreset is what the selector shows: the STORED selection of the cut preset
    // family (a preset id or PresetFamily.Custom), mirrored from the family and
    // persisted in EnergySegmenterSettings.CutPreset. Picking a preset writes its
    // eight values through the normal setters (each pushes to the settings);
    // picking Custom writes back the user's own values the family kept. A hand
    // edit of one of the eight values calls NotifyCutValueEdited, which switches
    // the selection to Custom and keeps the edited values. While the family
    // applies a preset it ignores those reports, so this view model needs no
    // guard of its own for that.
    //
    // The family is rebuilt in Load() (see there), hence not readonly.

    private PresetFamily<CutCurveValues> _cutPresets;

    [ObservableProperty]
    public partial string CutPreset { get; set; }

    partial void OnCutPresetChanged(string value)
    {
        if (_isSyncing) return;
        _cutPresets.Select(value);
    }

    private PresetFamily<CutCurveValues> BuildCutPresets(Func<EnergySegmenterSettings> segmenter, Action save) =>
        new(CutPresets.All, CutPresets.Default, new CutPresetStore(this, segmenter, save));

    // Called after each hand edit of one of the eight hangover values, once it
    // is pushed to the settings. The family copies the live values into the
    // Custom slot and stores Custom; the selector then follows. The mirror is
    // skipped while a preset is being applied: the store has not written the
    // new selection yet at that point, and mirroring would put the old one
    // back on the selector. _isSyncing keeps OnCutPresetChanged from calling
    // Select again for a change that came from the family.
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

