using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Input.Trackpad.ViewModels;

// ViewModel for TrackpadPage — bridges TrackpadSettings (master switch,
// drag speed, raw-frame recording, and the temporary tuning knobs) to the
// XAML via x:Bind. Same shape as Deckle.Settings' DiagnosticsViewModel :
// Load() pulls from the store, each property change pushes back via
// Push() + Save(), and the _isSyncing flag suppresses the write-back while
// Load() seeds the properties so re-hydration never re-saves.
//
// The Windows-integration acts (neutralize / repair / start elevated) are
// NOT modelled here : they are imperative commands with their own success
// reporting, driven straight from the page code-behind. Only the persisted
// settings live in this view-model.
public partial class TrackpadViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Three-finger drag ───────────────────────────────────────────────────

    // Master switch — the recognizer runs only when on. Doubles as the
    // SettingsExpander's IsExpanded source (OneWay) so the speed slider is
    // revealed exactly when the feature is on.
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // Linear speed multiplier applied to contact deltas before injection —
    // the single user-facing sensitivity control. DragSpeedLabel mirrors it
    // as a formatted string for the slider's value display.
    [ObservableProperty]
    public partial double DragSpeed { get; set; }

    // ── Diagnostics ─────────────────────────────────────────────────────────

    // Writes every raw contact frame to a JSONL file under the telemetry
    // folder, independent of the master switch.
    [ObservableProperty]
    public partial bool RecordFrames { get; set; }

    // ── Tuning (temporary) ──────────────────────────────────────────────────
    // Knobs exposed only while the defaults are calibrated on real sessions ;
    // frozen into engine constants (and removed from the page) afterwards.

    // Grace delay after the fingers lift before the drag releases, in ms.
    [ObservableProperty]
    public partial double GraceDelayMs { get; set; }

    // Start threshold shown to the user as a percentage of the pad width but
    // stored as a 0..1 ratio in TrackpadTuning.StartThresholdRatio. The ×100 /
    // ÷100 conversion lives in Load / Push so the slider can bind a plain
    // percent value with no converter in the XAML.
    [ObservableProperty]
    public partial double StartThresholdPercent { get; set; }

    // Baseline logical-units → mickeys factor the speed multiplier rides on.
    [ObservableProperty]
    public partial double BaseScale { get; set; }

    partial void OnEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        Push();
    }

    partial void OnDragSpeedChanged(double value)
    {
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
        if (_isSyncing) return;
        Push();
    }

    partial void OnStartThresholdPercentChanged(double value)
    {
        if (_isSyncing) return;
        Push();
    }

    partial void OnBaseScaleChanged(double value)
    {
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
            StartThresholdPercent = s.Tuning.StartThresholdRatio * 100.0;
            BaseScale = s.Tuning.BaseScale;
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
        s.DragSpeed = DragSpeed;
        s.RecordFrames = RecordFrames;
        s.Tuning.GraceDelayMs = (int)GraceDelayMs;
        s.Tuning.StartThresholdRatio = StartThresholdPercent / 100.0;
        s.Tuning.BaseScale = BaseScale;
        TrackpadSettingsService.Instance.Save();
    }
}
