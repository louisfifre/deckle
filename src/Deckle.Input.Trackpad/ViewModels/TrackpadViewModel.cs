using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Input.Trackpad.ViewModels;

// ViewModel for TrackpadPage — bridges TrackpadSettings (master switch,
// drag speed, raw-frame recording, and the temporary tuning knobs) to the
// XAML via x:Bind. Same shape as Deckle.Settings' DiagnosticsViewModel :
// Load() pulls from the store, each property change pushes back via
// Push() + Save(), and the _isSyncing flag suppresses the write-back while
// Load() seeds the properties so re-hydration never re-saves.
//
// Each slider value is mirrored by a formatted *Label string for the
// readout next to the slider — raw doubles must never reach a TextBlock
// (a snapped slider value can carry float noise like 1.3666670…).
// Push() rounds before persisting for the same reason.
//
// The Windows-integration acts (neutralize / repair / start elevated) are
// NOT modelled here : they are imperative commands with their own success
// reporting, driven straight from the page code-behind. Only the persisted
// settings live in this view-model.
public partial class TrackpadViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Three-finger drag ───────────────────────────────────────────────────

    // Master switch — the recognizer runs only when on. Also greys the
    // drag-speed card (IsEnabled, OneWay).
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // Linear speed multiplier applied to contact deltas before injection —
    // the single user-facing sensitivity control.
    [ObservableProperty]
    public partial double DragSpeed { get; set; }

    public string DragSpeedLabel =>
        DragSpeed.ToString("0.00", CultureInfo.CurrentCulture) + "×";

    // ── Diagnostics ─────────────────────────────────────────────────────────

    // Writes every raw contact frame to a JSONL file under the telemetry
    // folder, independent of the master switch.
    [ObservableProperty]
    public partial bool RecordFrames { get; set; }

    // ── Tuning (temporary) ──────────────────────────────────────────────────
    // Knobs exposed only while the defaults are calibrated on real sessions ;
    // frozen into engine constants (and removed from the page) afterwards.

    // Grace delay after the fingers lift before the drag releases, in ms.
    // 0 releases immediately.
    [ObservableProperty]
    public partial double GraceDelayMs { get; set; }

    public string GraceDelayLabel =>
        GraceDelayMs.ToString("0", CultureInfo.CurrentCulture) + " ms";

    // Start threshold shown to the user as a percentage of the pad width but
    // stored as a 0..1 ratio in TrackpadTuning.StartThresholdRatio. The ×100 /
    // ÷100 conversion lives in Load / Push so the slider can bind a plain
    // percent value with no converter in the XAML.
    [ObservableProperty]
    public partial double StartThresholdPercent { get; set; }

    public string StartThresholdLabel =>
        StartThresholdPercent.ToString("0.0", CultureInfo.CurrentCulture) + " %";

    partial void OnEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        Push();
    }

    partial void OnDragSpeedChanged(double value)
    {
        OnPropertyChanged(nameof(DragSpeedLabel));
        if (_isSyncing) return;
        Push();
    }

    partial void OnRecordFramesChanged(bool value)
    {
        if (_isSyncing) return;
        Push();
    }

    partial void OnGraceDelayMsChanged(double value)
    {
        OnPropertyChanged(nameof(GraceDelayLabel));
        if (_isSyncing) return;
        Push();
    }

    partial void OnStartThresholdPercentChanged(double value)
    {
        OnPropertyChanged(nameof(StartThresholdLabel));
        if (_isSyncing) return;
        Push();
    }

    // ── Sync with TrackpadSettingsService ───────────────────────────────────

    public TrackpadViewModel()
    {
        // Guard BEFORE any property assignment, like DiagnosticsViewModel —
        // the seed below must not be mistaken for user edits.
        _isSyncing = true;
        Load();
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var s = TrackpadSettingsService.Instance.Current;
            Enabled = s.Enabled;
            DragSpeed = s.DragSpeed;
            RecordFrames = s.RecordFrames;
            GraceDelayMs = s.Tuning.GraceDelayMs;
            StartThresholdPercent = Math.Round(s.Tuning.StartThresholdRatio * 100.0, 1);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void Push()
    {
        var s = TrackpadSettingsService.Instance.Current;
        s.Enabled = Enabled;
        s.DragSpeed = Math.Round(DragSpeed, 2);
        s.RecordFrames = RecordFrames;
        s.Tuning.GraceDelayMs = (int)Math.Round(GraceDelayMs);
        s.Tuning.StartThresholdRatio = Math.Round(StartThresholdPercent, 1) / 100.0;
        TrackpadSettingsService.Instance.Save();
    }
}
