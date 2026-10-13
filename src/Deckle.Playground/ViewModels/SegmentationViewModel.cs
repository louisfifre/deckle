using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Transcription;

namespace Deckle.Playground;

// Backs the Segmentation page: the energy segmenter's hangover curve and
// detection knobs (EnergySegmenterSettings).
//
// Deliberately not AmbientViewModel's live-save model: edits stay in the VM
// until Save. The VM holds the knobs in display units (seconds for the
// hangover and ramp durations, dBFS and ms for the rest), the persisted
// snapshot Revert returns to, and the compiled defaults Reset loads.
//
// The segmenter reads its settings at each streaming-session start, so a Save
// here applies on the next dictation without restarting Deckle.
public partial class SegmentationViewModel : ObservableObject
{
    // Guards the bulk writes in Apply so the per-property hooks don't
    // recompute the dirty flags mid-sync; Apply recomputes once at the end.
    private bool _isSyncing;

    private Snapshot _saved;             // what is persisted — Revert target
    private readonly Snapshot _defaults; // compiled defaults — Reset target

    // ── Hangover envelope (seconds) ──────────────────────────────────────────
    [ObservableProperty] public partial double HangoverMaxSec { get; set; }
    [ObservableProperty] public partial double HangoverMinSec { get; set; }
    [ObservableProperty] public partial double RampStartSec   { get; set; }
    [ObservableProperty] public partial double RampEndSec     { get; set; }

    // ── Curve shape — the two cubic-Bézier control points, each coord in [0,1] ──
    [ObservableProperty] public partial double CurveX1 { get; set; }
    [ObservableProperty] public partial double CurveY1 { get; set; }
    [ObservableProperty] public partial double CurveX2 { get; set; }
    [ObservableProperty] public partial double CurveY2 { get; set; }

    // ── Detection ────────────────────────────────────────────────────────────
    // MarginMs / MinUtteranceMs are double so they bind to TunableRow's double
    // Value; WriteTo rounds them back to whole ms for the store.
    [ObservableProperty] public partial double ThresholdDbfs  { get; set; }
    [ObservableProperty] public partial double MarginMs       { get; set; }
    [ObservableProperty] public partial double MinUtteranceMs { get; set; }

    // ── Command-enable flags ─────────────────────────────────────────────────
    [ObservableProperty] public partial bool IsDirty  { get; set; }
    [ObservableProperty] public partial bool CanReset { get; set; }

    public SegmentationViewModel()
    {
        _defaults = Snapshot.FromSettings(new EnergySegmenterSettings());
        // Load up front so the two-way bindings read the stored values on their
        // first evaluation, not the compiled defaults.
        Load();
    }

    partial void OnHangoverMaxSecChanged(double value) => Recompute();
    partial void OnHangoverMinSecChanged(double value) => Recompute();
    partial void OnRampStartSecChanged(double value)   => Recompute();
    partial void OnRampEndSecChanged(double value)     => Recompute();
    partial void OnCurveX1Changed(double value)        => Recompute();
    partial void OnCurveY1Changed(double value)        => Recompute();
    partial void OnCurveX2Changed(double value)        => Recompute();
    partial void OnCurveY2Changed(double value)        => Recompute();
    partial void OnThresholdDbfsChanged(double value)  => Recompute();
    partial void OnMarginMsChanged(double value)       => Recompute();
    partial void OnMinUtteranceMsChanged(double value) => Recompute();

    private void Recompute()
    {
        if (_isSyncing) return;
        var cur = Capture();
        IsDirty  = !cur.Equals(_saved);
        CanReset = !cur.Equals(_defaults);
    }

    // ── Public operations ────────────────────────────────────────────────────

    // Makes the persisted settings the editor's values and its baseline.
    public void Load()
    {
        _saved = Snapshot.FromSettings(TranscriptionSettingsService.Instance.Current.Streaming.Segmenter);
        Apply(_saved);
    }

    public void Save()
    {
        var seg = TranscriptionSettingsService.Instance.Current.Streaming.Segmenter;
        var cur = Capture();
        cur.WriteTo(seg);
        TranscriptionSettingsService.Instance.Save();
        _saved = cur;
        Recompute();
    }

    public void Revert()        => Apply(_saved);
    public void ResetDefaults() => Apply(_defaults);

    // Per-section resets set only their own knobs, so the other unsaved edits
    // survive.
    public void ResetHangoverRamp()
    {
        HangoverMaxSec = _defaults.HangoverMaxSec;
        HangoverMinSec = _defaults.HangoverMinSec;
        RampStartSec   = _defaults.RampStartSec;
        RampEndSec     = _defaults.RampEndSec;
    }

    public void ResetCurve()
    {
        CurveX1 = _defaults.CurveX1;
        CurveY1 = _defaults.CurveY1;
        CurveX2 = _defaults.CurveX2;
        CurveY2 = _defaults.CurveY2;
    }

    public void ResetDetection()
    {
        ThresholdDbfs  = _defaults.ThresholdDbfs;
        MarginMs       = _defaults.MarginMs;
        MinUtteranceMs = _defaults.MinUtteranceMs;
    }

    // ── Snapshot plumbing ────────────────────────────────────────────────────

    private Snapshot Capture() => new(
        HangoverMaxSec, HangoverMinSec, RampStartSec, RampEndSec,
        CurveX1, CurveY1, CurveX2, CurveY2, ThresholdDbfs, MarginMs, MinUtteranceMs);

    private void Apply(Snapshot s)
    {
        bool prev = _isSyncing;
        _isSyncing = true;
        try
        {
            HangoverMaxSec = s.HangoverMaxSec;
            HangoverMinSec = s.HangoverMinSec;
            RampStartSec   = s.RampStartSec;
            RampEndSec     = s.RampEndSec;
            CurveX1        = s.CurveX1;
            CurveY1        = s.CurveY1;
            CurveX2        = s.CurveX2;
            CurveY2        = s.CurveY2;
            ThresholdDbfs  = s.ThresholdDbfs;
            MarginMs       = s.MarginMs;
            MinUtteranceMs = s.MinUtteranceMs;
        }
        finally
        {
            _isSyncing = prev;
        }
        Recompute();
    }

    // The knobs in display units, with the ms ↔ seconds conversions in one
    // place. The dirty check relies on the record struct's field-wise equality.
    private readonly record struct Snapshot(
        double HangoverMaxSec, double HangoverMinSec, double RampStartSec, double RampEndSec,
        double CurveX1, double CurveY1, double CurveX2, double CurveY2,
        double ThresholdDbfs, double MarginMs, double MinUtteranceMs)
    {
        public static Snapshot FromSettings(EnergySegmenterSettings s) => new(
            HangoverMaxSec: s.HangoverMaxMs / 1000.0,
            HangoverMinSec: s.HangoverMinMs / 1000.0,
            RampStartSec:   s.HangoverRampStartMs / 1000.0,
            RampEndSec:     s.HangoverRampEndMs / 1000.0,
            CurveX1:        s.HangoverCurveX1,
            CurveY1:        s.HangoverCurveY1,
            CurveX2:        s.HangoverCurveX2,
            CurveY2:        s.HangoverCurveY2,
            ThresholdDbfs:  s.ThresholdDbfs,
            MarginMs:       s.MarginMs,
            MinUtteranceMs: s.MinUtteranceMs);

        public void WriteTo(EnergySegmenterSettings s)
        {
            s.HangoverMaxMs       = (int)Math.Round(HangoverMaxSec * 1000.0);
            s.HangoverMinMs       = (int)Math.Round(HangoverMinSec * 1000.0);
            s.HangoverRampStartMs = (int)Math.Round(RampStartSec * 1000.0);
            s.HangoverRampEndMs   = (int)Math.Round(RampEndSec * 1000.0);
            s.HangoverCurveX1     = CurveX1;
            s.HangoverCurveY1     = CurveY1;
            s.HangoverCurveX2     = CurveX2;
            s.HangoverCurveY2     = CurveY2;
            s.ThresholdDbfs       = ThresholdDbfs;
            s.MarginMs            = (int)Math.Round(MarginMs);
            s.MinUtteranceMs      = (int)Math.Round(MinUtteranceMs);
        }
    }
}
