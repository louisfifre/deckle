using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Diagnostics.Logging;

// Load() pulls from each store; property changes push back through the
// matching PushXxxToSettings(), and _isSyncing prevents re-saving during
// Load(). A toggle rewrites only its own store: the logging and telemetry
// files share neither schema nor lifecycle.
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

    [ObservableProperty]
    public partial bool LogWindowingActivity { get; set; }

    // ── Telemetry — opt-in disk persistence ─────────────────────────────────

    [ObservableProperty]
    public partial bool ApplicationLogToDisk { get; set; }

    [ObservableProperty]
    public partial bool RecordWheelEvents { get; set; }

    // Empty means AppPaths.TelemetryDirectory.
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
        // Guard before any property assignment, or each change handler would
        // save these placeholders over the stored settings.
        _isSyncing = true;

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
