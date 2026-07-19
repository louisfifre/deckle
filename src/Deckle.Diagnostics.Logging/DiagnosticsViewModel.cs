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

    [ObservableProperty]
    public partial bool LogAmbientCaptureActivity { get; set; }

    [ObservableProperty]
    public partial bool LogTranscriptionActivity { get; set; }

    [ObservableProperty]
    public partial bool LogAutocorrectActivity { get; set; }

    [ObservableProperty]
    public partial bool LogInputActivity { get; set; }

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

    [ObservableProperty]
    public partial bool RecordWheelEvents { get; set; }

    // Storage folder override — empty = AppPaths.TelemetryDirectory.
    // FolderPickerCard.DefaultPath is wired to the resolved default in
    // the page code-behind ; the picker shows it as a placeholder when
    // the override is empty.
    [ObservableProperty]
    public partial string TelemetryStorageDirectory { get; set; }

    partial void OnLogAmbientCaptureActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogAmbientCaptureActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnLogTranscriptionActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogTranscriptionActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnLogAutocorrectActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogAutocorrectActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnLogInputActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogInputActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnLogWindowingActivityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.LogWindowingActivity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnApplicationLogToDiskChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.ApplicationLogToDisk", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnRecordWheelEventsChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Input.RecordWheelEvents", value.ToString());
        var settings = Deckle.Input.MouseWheelSettingsService.Instance.Current;
        settings.RecordEvents = value;
        Deckle.Input.MouseWheelSettingsService.Instance.Save();
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
        LogAmbientCaptureActivity = false;
        LogTranscriptionActivity = false;
        LogAutocorrectActivity = false;
        LogInputActivity = false;
        LogWindowingActivity = false;
        ApplicationLogToDisk = false;
        RecordWheelEvents = false;
        TelemetryStorageDirectory = "";

        // _isSyncing stays true — Load() will set it to false.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var l = LoggingSettingsService.Instance.Current;
            LogAmbientCaptureActivity = l.LogAmbientCaptureActivity;
            LogTranscriptionActivity = l.LogTranscriptionActivity;
            LogAutocorrectActivity = l.LogAutocorrectActivity;
            LogInputActivity = l.LogInputActivity;
            LogWindowingActivity = l.LogWindowingActivity;
            ApplicationLogToDisk = l.ApplicationLogToDisk;
            RecordWheelEvents = Deckle.Input.MouseWheelSettingsService.Instance.Current.RecordEvents;

            var t = TelemetrySettingsService.Instance.Current;
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
        l.LogAmbientCaptureActivity = LogAmbientCaptureActivity;
        l.LogTranscriptionActivity = LogTranscriptionActivity;
        l.LogAutocorrectActivity = LogAutocorrectActivity;
        l.LogInputActivity = LogInputActivity;
        l.LogWindowingActivity = LogWindowingActivity;
        l.ApplicationLogToDisk = ApplicationLogToDisk;
        LoggingSettingsService.Instance.Save();
    }

    private void PushTelemetryToSettings()
    {
        var t = TelemetrySettingsService.Instance.Current;
        t.StorageDirectory = TelemetryStorageDirectory ?? "";
        TelemetrySettingsService.Instance.Save();
    }

    // ── Reset ───────────────────────────────────────────────────────────────

    public void ResetLoggingDefaults()
    {
        _isSyncing = true;
        try
        {
            LogAmbientCaptureActivity = false;
            LogTranscriptionActivity = false;
            LogAutocorrectActivity = false;
            LogInputActivity = false;
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
            TelemetryStorageDirectory = "";
            RecordWheelEvents = false;
        }
        finally { _isSyncing = false; }
        PushTelemetryToSettings();
        var mouseWheel = Deckle.Input.MouseWheelSettingsService.Instance.Current;
        mouseWheel.RecordEvents = false;
        Deckle.Input.MouseWheelSettingsService.Instance.Save();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Telemetry");
    }

    public void ResetApplicationLogDefaults()
    {
        _isSyncing = true;
        try
        {
            ApplicationLogToDisk = false;
        }
        finally { _isSyncing = false; }
        PushLoggingToSettings();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("ApplicationLog");
    }
}
