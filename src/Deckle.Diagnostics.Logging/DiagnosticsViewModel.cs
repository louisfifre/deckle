using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Diagnostics.Logging;

// ViewModel for DiagnosticsPage — bridges TelemetrySettings and
// LoggingSettings to the XAML via x:Bind. Originally migrated from
// GeneralViewModel in slice S2 (Telemetry only) ; J4 polish added the
// Logging section to host runtime emission filters orthogonal to
// disk persistence, which expanded the VM to cover two stores.
//
// Pattern : Load() pulls from each store, property changes push back
// via the matching PushXxxToSettings(). The _isSyncing flag prevents
// re-saving during Load(). The split between PushLoggingToSettings()
// and PushTelemetryToSettings() lets a single toggle touch only its
// own store — flipping Verbose logging doesn't rewrite the telemetry
// JSON file, which matters because the two share neither schema nor
// lifecycle.
public partial class DiagnosticsViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Logging — runtime emission filters ──────────────────────────────────

    // Windowing Verbose toggle: when off (default), the whole Deckle-Windowing
    // firehose is dropped — placement, overlay slots, popup anchoring, z-order,
    // resize frames, first-open timings. The provider emits Verbose only, so off
    // means a fully silent channel; on surfaces everything for a placement /
    // resize-lag dive. No capture window — the windows exist continuously.
    [ObservableProperty]
    public partial bool LogWindowingActivity { get; set; }

    // ── Telemetry — opt-in disk persistence ─────────────────────────────────

    // Application log — mirrors every in-app log line to app.jsonl. Top of
    // section by user request : the most asked-for diagnostic when
    // troubleshooting an issue across restarts.
    [ObservableProperty]
    public partial bool ApplicationLogToDisk { get; set; }

    // Storage folder override — empty = AppPaths.TelemetryDirectory.
    // FolderPickerCard.DefaultPath is wired to the resolved default in
    // the page code-behind ; the picker shows it as a placeholder when
    // the override is empty.
    [ObservableProperty]
    public partial string TelemetryStorageDirectory { get; set; }

    partial void OnLogWindowingActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogWindowingActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnApplicationLogToDiskChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.ApplicationLogToDisk", value.ToString());
        PushTelemetryToSettings();
    }

    partial void OnTelemetryStorageDirectoryChanged(string value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.StorageDirectory", $"\"{value}\"");
        PushTelemetryToSettings();
    }

    // ── Sync with LoggingSettingsService and TelemetrySettingsService ───────

    public DiagnosticsViewModel()
    {
        // Guard BEFORE any property assignment — same reason as GeneralViewModel.
        _isSyncing = true;

        // Logging defaults are "closed" by family : every per-loop
        // capture toggle starts OFF because the routine cadence
        // drowns out everything else. Non-Verbose levels and
        // out-of-loop emissions are unaffected, so milestones,
        // errors, and user actions stay visible — only the per-tick
        // noise is suppressed. Telemetry defaults are also "closed"
        // but for a different reason : disk-persistence streams stay
        // off until the user explicitly opts in to where their data
        // lands.
        LogWindowingActivity = false;
        ApplicationLogToDisk = false;
        TelemetryStorageDirectory = "";

        // _isSyncing stays true — Load() will set it to false.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var l = LoggingSettingsService.Instance.Current;
            LogWindowingActivity = l.LogWindowingActivity;

            var t = TelemetrySettingsService.Instance.Current;
            ApplicationLogToDisk = t.ApplicationLogToDisk;
            TelemetryStorageDirectory = t.StorageDirectory;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void PushLoggingToSettings()
    {
        var l = LoggingSettingsService.Instance.Current;
        l.LogWindowingActivity = LogWindowingActivity;
        LoggingSettingsService.Instance.Save();
    }

    private void PushTelemetryToSettings()
    {
        var t = TelemetrySettingsService.Instance.Current;
        t.ApplicationLogToDisk = ApplicationLogToDisk;
        t.StorageDirectory = TelemetryStorageDirectory ?? "";
        TelemetrySettingsService.Instance.Save();
    }

    // ── Reset ───────────────────────────────────────────────────────────────

    public void ResetLoggingDefaults()
    {
        _isSyncing = true;
        try
        {
            LogWindowingActivity = false;
        }
        finally { _isSyncing = false; }
        PushLoggingToSettings();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Logging");
    }

    public void ResetTelemetryDefaults()
    {
        _isSyncing = true;
        try
        {
            ApplicationLogToDisk = false;
            TelemetryStorageDirectory = "";
        }
        finally { _isSyncing = false; }
        PushTelemetryToSettings();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Telemetry");
    }
}
