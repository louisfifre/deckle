using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.Input;
using Deckle.Input.Trackpad;

namespace Deckle.Diagnostics.Logging.Views;

// Load() pulls from each store; a property change pushes back to the one store
// that owns it, and _isSyncing prevents re-saving during Load(). The values
// live where their consumers read them (logging, telemetry, mouse-wheel and
// trackpad stores); this page is their one editor.
public partial class DiagnosticsViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Logs ────────────────────────────────────────────────────────────────

    [ObservableProperty] public partial bool DetailedLogs { get; set; }
    [ObservableProperty] public partial bool AmbientDetails { get; set; }
    [ObservableProperty] public partial bool TranscriptionDetails { get; set; }
    [ObservableProperty] public partial bool AutocorrectDetails { get; set; }
    [ObservableProperty] public partial bool InputDetails { get; set; }
    [ObservableProperty] public partial bool WindowingDetails { get; set; }
    [ObservableProperty] public partial bool LlmDetails { get; set; }

    // Developer mode lives in the Settings shell, which this module does not
    // reference; the composition root hands its reader in. Read at each
    // Load(), so a visit after the mode changed shows or hides the captures.
    public static Func<bool> DeveloperModeReader { get; set; } = static () => false;

    // Not a setting: the developer captures below show only while it is true,
    // and record only while it is true too (App.DeveloperCapturesAdmitted).
    [ObservableProperty] public partial bool DeveloperMode { get; set; }

    // The developer captures under Detailed logs, each in its own store.
    [ObservableProperty] public partial bool DictationTimings { get; set; }
    [ObservableProperty] public partial bool MicrophoneLevel { get; set; }
    [ObservableProperty] public partial bool RecordWheelEvents { get; set; }
    [ObservableProperty] public partial bool RecordTouchpadFrames { get; set; }

    [ObservableProperty] public partial bool ApplicationLogToDisk { get; set; }

    // Empty means AppPaths.TelemetryDirectory.
    [ObservableProperty] public partial string TelemetryStorageDirectory { get; set; }

    partial void OnDetailedLogsChanged(bool value) => PushLogging(nameof(LoggingSettings.DetailedLogs), value);
    partial void OnAmbientDetailsChanged(bool value) => PushLogging(nameof(LoggingSettings.AmbientDetails), value);
    partial void OnTranscriptionDetailsChanged(bool value) => PushLogging(nameof(LoggingSettings.TranscriptionDetails), value);
    partial void OnAutocorrectDetailsChanged(bool value) => PushLogging(nameof(LoggingSettings.AutocorrectDetails), value);
    partial void OnInputDetailsChanged(bool value) => PushLogging(nameof(LoggingSettings.InputDetails), value);
    partial void OnWindowingDetailsChanged(bool value) => PushLogging(nameof(LoggingSettings.WindowingDetails), value);
    partial void OnLlmDetailsChanged(bool value) => PushLogging(nameof(LoggingSettings.LlmDetails), value);
    partial void OnApplicationLogToDiskChanged(bool value) => PushLogging(nameof(LoggingSettings.ApplicationLogToDisk), value);

    partial void OnDictationTimingsChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.LatencyEnabled", value.ToString());
        PushTelemetry();
    }

    partial void OnMicrophoneLevelChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.MicrophoneTelemetry", value.ToString());
        PushTelemetry();
    }

    partial void OnRecordWheelEventsChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Input.RecordWheelEvents", value.ToString());
        MouseWheelSettingsService.Instance.Current.RecordEvents = value;
        MouseWheelSettingsService.Instance.Save();
    }

    partial void OnRecordTouchpadFramesChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Trackpad.RecordFrames", value.ToString());
        TrackpadSettingsService.Instance.Current.RecordFrames = value;
        TrackpadSettingsService.Instance.Save();
    }

    partial void OnTelemetryStorageDirectoryChanged(string value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.StorageDirectory", $"\"{value}\"");
        PushTelemetry();
    }

    // ── Sync with the stores ────────────────────────────────────────────────

    public DiagnosticsViewModel()
    {
        // Guard before any property assignment, or each change handler would
        // save these placeholders over the stored settings. The placeholders
        // read the defaults from the classes that own them, the same source
        // the resets below use. _isSyncing stays true: Load() clears it.
        _isSyncing = true;
        Apply(new LoggingSettings(), new TelemetrySettings(),
            new MouseWheelSettings(), new TrackpadSettings());
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            Apply(LoggingSettingsService.Instance.Current,
                TelemetrySettingsService.Instance.Current,
                MouseWheelSettingsService.Instance.Current,
                TrackpadSettingsService.Instance.Current);
            DeveloperMode = DeveloperModeReader();
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void Apply(LoggingSettings l, TelemetrySettings t, MouseWheelSettings wheel, TrackpadSettings trackpad)
    {
        DetailedLogs = l.DetailedLogs;
        AmbientDetails = l.AmbientDetails;
        TranscriptionDetails = l.TranscriptionDetails;
        AutocorrectDetails = l.AutocorrectDetails;
        InputDetails = l.InputDetails;
        WindowingDetails = l.WindowingDetails;
        LlmDetails = l.LlmDetails;
        ApplicationLogToDisk = l.ApplicationLogToDisk;
        DictationTimings = t.LatencyEnabled;
        MicrophoneLevel = t.MicrophoneTelemetry;
        TelemetryStorageDirectory = t.StorageDirectory;
        RecordWheelEvents = wheel.RecordEvents;
        RecordTouchpadFrames = trackpad.RecordFrames;
    }

    private void PushLogging(string setting, object value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged($"Logging.{setting}", value.ToString() ?? "");
        var l = LoggingSettingsService.Instance.Current;
        l.DetailedLogs = DetailedLogs;
        l.AmbientDetails = AmbientDetails;
        l.TranscriptionDetails = TranscriptionDetails;
        l.AutocorrectDetails = AutocorrectDetails;
        l.InputDetails = InputDetails;
        l.WindowingDetails = WindowingDetails;
        l.LlmDetails = LlmDetails;
        l.ApplicationLogToDisk = ApplicationLogToDisk;
        LoggingSettingsService.Instance.Save();
    }

    private void PushTelemetry()
    {
        var t = TelemetrySettingsService.Instance.Current;
        t.LatencyEnabled = DictationTimings;
        t.MicrophoneTelemetry = MicrophoneLevel;
        t.StorageDirectory = TelemetryStorageDirectory ?? "";
        TelemetrySettingsService.Instance.Save();
    }

    // ── Reset ───────────────────────────────────────────────────────────────

    // Every value of the page's own sections back to its default, through the
    // setters, so each store saves once per changed value like a person's
    // flip. The defaults are off for every capture and every opt-in, so a
    // reset never turns on what a consent dialog guards.
    public void ResetLogsDefaults()
    {
        var l = new LoggingSettings();
        var t = new TelemetrySettings();
        DetailedLogs = l.DetailedLogs;
        AmbientDetails = l.AmbientDetails;
        TranscriptionDetails = l.TranscriptionDetails;
        AutocorrectDetails = l.AutocorrectDetails;
        InputDetails = l.InputDetails;
        WindowingDetails = l.WindowingDetails;
        LlmDetails = l.LlmDetails;
        DictationTimings = t.LatencyEnabled;
        MicrophoneLevel = t.MicrophoneTelemetry;
        RecordWheelEvents = new MouseWheelSettings().RecordEvents;
        RecordTouchpadFrames = new TrackpadSettings().RecordFrames;
        ApplicationLogToDisk = l.ApplicationLogToDisk;
    }

    public void ResetFoldersDefaults()
        => TelemetryStorageDirectory = new TelemetrySettings().StorageDirectory;
}
