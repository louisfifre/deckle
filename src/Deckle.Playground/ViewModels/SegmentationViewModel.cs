using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Transcription;

namespace Deckle.Playground;

// ─── Streaming segmentation tuning ViewModel ─────────────────────────────────
//
// Backs the Segmentation Playground page : the dynamic-hangover curve editor
// plus the detection knobs of the energy segmenter (EnergySegmenterSettings).
//
// DELIBERATELY NOT the AmbientViewModel live-save model. Ambient writes every
// slider change straight into its store ; here edits stay EPHEMERAL in the VM
// until the user presses Save (the Playground Save / Revert / Reset contract).
// The VM holds the ten knobs in display units (seconds for the hangover / ramp
// durations, dBFS / ms for the rest), a snapshot of what is currently persisted
// (the baseline Revert returns to) and the compiled defaults (what Reset loads).
// IsDirty / CanReset drive the footer buttons.
//
// Persistence lands in <UserDataRoot>/modules/transcription/settings.json via
// TranscriptionSettingsService. The segmenter reads its settings fresh at each
// streaming-session start, so a Save here applies on the next dictation without
// restarting Deckle.
public partial class SegmentationViewModel : ObservableObject
{
    // Guards the bulk writes in Load / Revert / ResetDefaults so the per-property
    // change hooks don't recompute the dirty flags mid-sync (we recompute once at
    // the end). Mirrors the _isSyncing guard in AmbientViewModel.
    private bool _isSyncing;

    private Snapshot _saved;             // what is persisted — Revert target
    private readonly Snapshot _defaults; // compiled defaults — Reset target

    // ── Hangover envelope (seconds) ──────────────────────────────────────────
    [ObservableProperty] public partial double HangoverMaxSec { get; set; }
    [ObservableProperty] public partial double HangoverMinSec { get; set; }
    [ObservableProperty] public partial double RampStartSec   { get; set; }
    [ObservableProperty] public partial double RampEndSec     { get; set; }

    // ── Curve shape ──────────────────────────────────────────────────────────
    [ObservableProperty] public partial double Contrast  { get; set; }
    [ObservableProperty] public partial double Position  { get; set; }
    [ObservableProperty] public partial double Sharpness { get; set; }

    // ── Detection ────────────────────────────────────────────────────────────
    [ObservableProperty] public partial double ThresholdDbfs  { get; set; }
    [ObservableProperty] public partial int    MarginMs       { get; set; }
    [ObservableProperty] public partial int    MinUtteranceMs { get; set; }

    // ── Command-enable flags ─────────────────────────────────────────────────
    [ObservableProperty] public partial bool IsDirty  { get; set; }
    [ObservableProperty] public partial bool CanReset { get; set; }

    public SegmentationViewModel()
    {
        _defaults = Snapshot.FromSettings(new EnergySegmenterSettings());
        _saved    = _defaults; // replaced by the first Load()
        Apply(_defaults);
    }

    // Every editable knob recomputes the dirty / reset flags on change. The flags
    // themselves are not knobs, so they don't recurse.
    partial void OnHangoverMaxSecChanged(double value) => Recompute();
    partial void OnHangoverMinSecChanged(double value) => Recompute();
    partial void OnRampStartSecChanged(double value)   => Recompute();
    partial void OnRampEndSecChanged(double value)     => Recompute();
    partial void OnContrastChanged(double value)       => Recompute();
    partial void OnPositionChanged(double value)       => Recompute();
    partial void OnSharpnessChanged(double value)      => Recompute();
    partial void OnThresholdDbfsChanged(double value)  => Recompute();
    partial void OnMarginMsChanged(int value)          => Recompute();
    partial void OnMinUtteranceMsChanged(int value)    => Recompute();

    private void Recompute()
    {
        if (_isSyncing) return;
        var cur = Capture();
        IsDirty  = !cur.Equals(_saved);
        CanReset = !cur.Equals(_defaults);
    }

    // ── Public operations ────────────────────────────────────────────────────

    // Pull the persisted segmenter settings into the editor and make them the
    // baseline. Called once when the page first loads.
    public void Load()
    {
        _saved = Snapshot.FromSettings(TranscriptionSettingsService.Instance.Current.Streaming.Segmenter);
        Apply(_saved);
    }

    // Push the current edits into the store and persist. The baseline becomes the
    // just-saved state, so IsDirty drops to false.
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

    // Adopt these (already slider-grid-coerced) values as BOTH the live state and
    // the saved baseline, with no per-property recompute churn. Used once at page
    // load to reconcile a persisted off-grid value with the slider grid, so the
    // thumb, the VM and the readouts agree and IsDirty starts false.
    public void AdoptAsSaved(
        double hangoverMaxSec, double hangoverMinSec, double rampStartSec, double rampEndSec,
        double contrast, double position, double sharpness,
        double thresholdDbfs, int marginMs, int minUtteranceMs)
    {
        _saved = new Snapshot(
            hangoverMaxSec, hangoverMinSec, rampStartSec, rampEndSec,
            contrast, position, sharpness, thresholdDbfs, marginMs, minUtteranceMs);
        Apply(_saved);
    }

    // ── Snapshot plumbing ────────────────────────────────────────────────────

    private Snapshot Capture() => new(
        HangoverMaxSec, HangoverMinSec, RampStartSec, RampEndSec,
        Contrast, Position, Sharpness, ThresholdDbfs, MarginMs, MinUtteranceMs);

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
            Contrast       = s.Contrast;
            Position       = s.Position;
            Sharpness      = s.Sharpness;
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

    // Value-equal struct snapshot of the ten knobs in display units, with the
    // ms ↔ seconds conversions kept in one place. record struct gives us the
    // field-wise equality the dirty check relies on.
    private readonly record struct Snapshot(
        double HangoverMaxSec, double HangoverMinSec, double RampStartSec, double RampEndSec,
        double Contrast, double Position, double Sharpness,
        double ThresholdDbfs, int MarginMs, int MinUtteranceMs)
    {
        public static Snapshot FromSettings(EnergySegmenterSettings s) => new(
            HangoverMaxSec: s.HangoverMaxMs / 1000.0,
            HangoverMinSec: s.HangoverMinMs / 1000.0,
            RampStartSec:   s.HangoverRampStartMs / 1000.0,
            RampEndSec:     s.HangoverRampEndMs / 1000.0,
            Contrast:       s.HangoverContrast,
            Position:       s.HangoverPosition,
            Sharpness:      s.HangoverSharpness,
            ThresholdDbfs:  s.ThresholdDbfs,
            MarginMs:       s.MarginMs,
            MinUtteranceMs: s.MinUtteranceMs);

        public void WriteTo(EnergySegmenterSettings s)
        {
            s.HangoverMaxMs       = (int)Math.Round(HangoverMaxSec * 1000.0);
            s.HangoverMinMs       = (int)Math.Round(HangoverMinSec * 1000.0);
            s.HangoverRampStartMs = (int)Math.Round(RampStartSec * 1000.0);
            s.HangoverRampEndMs   = (int)Math.Round(RampEndSec * 1000.0);
            s.HangoverContrast    = Contrast;
            s.HangoverPosition    = Position;
            s.HangoverSharpness   = Sharpness;
            s.ThresholdDbfs       = ThresholdDbfs;
            s.MarginMs            = MarginMs;
            s.MinUtteranceMs      = MinUtteranceMs;
        }
    }
}
