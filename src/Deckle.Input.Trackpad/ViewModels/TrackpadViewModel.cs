using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Input.Trackpad;

// ViewModel for TrackpadPage — bridges TrackpadSettings (master switch,
// drag speed, raw-frame recording) to the XAML via x:Bind. Same shape as
// Deckle.Settings' DiagnosticsViewModel :
// Load() pulls from the store, each property change pushes back via
// Push() + Save(), and the _isSyncing flag suppresses the write-back while
// Load() seeds the properties so re-hydration never re-saves.
//
// The drag-speed readout is no longer a *Label string on this VM: the
// SettingsComposer renders the slider's value itself (rounded per its
// StepFrequency) with the "×" unit, so the former DragSpeedLabel is gone.
// Push() still rounds before persisting so a snapped slider value's float
// noise (1.3666670…) never reaches the store.
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

    // ── Diagnostics ─────────────────────────────────────────────────────────

    // Writes every raw contact frame to a JSONL file under the telemetry
    // folder, independent of the master switch.
    [ObservableProperty]
    public partial bool RecordFrames { get; set; }

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
        TrackpadSettingsService.Instance.Save();
    }
}
