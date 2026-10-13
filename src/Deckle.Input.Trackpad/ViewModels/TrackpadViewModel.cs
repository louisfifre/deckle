using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Input.Trackpad;

// The _isSyncing flag suppresses the write-back while Load() seeds the
// properties, so re-hydration never re-saves. Push() rounds before persisting
// so a snapped slider value's float noise (1.3666670…) never reaches the store.
public partial class TrackpadViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Three-finger drag ───────────────────────────────────────────────────

    // Master switch — the recognizer runs only when on.
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
        // Guard BEFORE any property assignment: the seed below must not be
        // mistaken for user edits.
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
