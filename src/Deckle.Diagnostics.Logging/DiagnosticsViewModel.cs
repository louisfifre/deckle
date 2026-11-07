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
    public partial OperationalLogVerbosity AmbientVerbosity { get; set; }

    [ObservableProperty]
    public partial OperationalLogVerbosity TranscriptionVerbosity { get; set; }

    [ObservableProperty]
    public partial OperationalLogVerbosity AutocorrectVerbosity { get; set; }

    [ObservableProperty]
    public partial OperationalLogVerbosity InputVerbosity { get; set; }

    [ObservableProperty]
    public partial OperationalLogVerbosity WindowingVerbosity { get; set; }

    // ── Telemetry — opt-in disk persistence ─────────────────────────────────

    [ObservableProperty]
    public partial bool ApplicationLogToDisk { get; set; }

    [ObservableProperty]
    public partial bool RecordWheelEvents { get; set; }

    // Empty means AppPaths.TelemetryDirectory.
    [ObservableProperty]
    public partial string TelemetryStorageDirectory { get; set; }

    partial void OnAmbientVerbosityChanged(OperationalLogVerbosity value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.AmbientVerbosity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnTranscriptionVerbosityChanged(OperationalLogVerbosity value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.TranscriptionVerbosity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnAutocorrectVerbosityChanged(OperationalLogVerbosity value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.AutocorrectVerbosity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnInputVerbosityChanged(OperationalLogVerbosity value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.InputVerbosity", value.ToString());
        PushLoggingToSettings();
    }

    partial void OnWindowingVerbosityChanged(OperationalLogVerbosity value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Logging.WindowingVerbosity", value.ToString());
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

        // The placeholders read the defaults from the settings classes that
        // own them, the same source the resets below use.
        var logging = new LoggingSettings();
        AmbientVerbosity = logging.AmbientVerbosity;
        TranscriptionVerbosity = logging.TranscriptionVerbosity;
        AutocorrectVerbosity = logging.AutocorrectVerbosity;
        InputVerbosity = logging.InputVerbosity;
        WindowingVerbosity = logging.WindowingVerbosity;
        ApplicationLogToDisk = logging.ApplicationLogToDisk;
        RecordWheelEvents = new Deckle.Input.MouseWheelSettings().RecordEvents;
        TelemetryStorageDirectory = new TelemetrySettings().StorageDirectory;

        // _isSyncing stays true — Load() will set it to false.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var l = LoggingSettingsService.Instance.Current;
            AmbientVerbosity = l.AmbientVerbosity;
            TranscriptionVerbosity = l.TranscriptionVerbosity;
            AutocorrectVerbosity = l.AutocorrectVerbosity;
            InputVerbosity = l.InputVerbosity;
            WindowingVerbosity = l.WindowingVerbosity;
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
        l.AmbientVerbosity = AmbientVerbosity;
        l.TranscriptionVerbosity = TranscriptionVerbosity;
        l.AutocorrectVerbosity = AutocorrectVerbosity;
        l.InputVerbosity = InputVerbosity;
        l.WindowingVerbosity = WindowingVerbosity;
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

    // Each reset reads its defaults from a fresh settings object, the same
    // source the descriptors' defaultValue reads. A default changed in the
    // settings class then moves the section reset and the value reset together.

    public void ResetLoggingDefaults()
    {
        var defaults = new LoggingSettings();
        _isSyncing = true;
        try
        {
            AmbientVerbosity = defaults.AmbientVerbosity;
            TranscriptionVerbosity = defaults.TranscriptionVerbosity;
            AutocorrectVerbosity = defaults.AutocorrectVerbosity;
            InputVerbosity = defaults.InputVerbosity;
            WindowingVerbosity = defaults.WindowingVerbosity;
        }
        finally { _isSyncing = false; }
        PushLoggingToSettings();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Logging");
    }

    public void ResetTelemetryDefaults()
    {
        var telemetryDefaults = new TelemetrySettings();
        var mouseWheelDefaults = new Deckle.Input.MouseWheelSettings();
        _isSyncing = true;
        try
        {
            TelemetryStorageDirectory = telemetryDefaults.StorageDirectory;
            RecordWheelEvents = mouseWheelDefaults.RecordEvents;
        }
        finally { _isSyncing = false; }
        PushTelemetryToSettings();
        var mouseWheel = Deckle.Input.MouseWheelSettingsService.Instance.Current;
        mouseWheel.RecordEvents = mouseWheelDefaults.RecordEvents;
        Deckle.Input.MouseWheelSettingsService.Instance.Save();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Telemetry");
    }

    public void ResetApplicationLogDefaults()
    {
        var defaults = new LoggingSettings();
        _isSyncing = true;
        try
        {
            ApplicationLogToDisk = defaults.ApplicationLogToDisk;
        }
        finally { _isSyncing = false; }
        PushLoggingToSettings();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("ApplicationLog");
    }
}
