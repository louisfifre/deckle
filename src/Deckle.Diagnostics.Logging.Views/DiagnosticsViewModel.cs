using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Diagnostics.Logging.Views;

// Load() pulls from each store; a property change pushes back to the one store
// that owns it, and _isSyncing prevents re-saving during Load(). The values
// live where their consumers read them (logging and telemetry stores); this
// page is their one editor.
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

    // Not a setting: the developer capture below shows only while it is true,
    // and records only while it is true too (App.DeveloperCapturesAdmitted).
    [ObservableProperty] public partial bool DeveloperMode { get; set; }

    // The developer capture under Detailed logs, in the telemetry store.
    [ObservableProperty] public partial bool MicrophoneLevel { get; set; }

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

    partial void OnMicrophoneLevelChanged(bool value)
    {
        if (_isSyncing) return;
        PushTelemetry();
    }

    partial void OnTelemetryStorageDirectoryChanged(string value)
    {
        if (_isSyncing) return;
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
        Apply(new LoggingSettings(), new TelemetrySettings());
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            Apply(LoggingSettingsService.Instance.Current,
                TelemetrySettingsService.Instance.Current);
            DeveloperMode = DeveloperModeReader();
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void Apply(LoggingSettings l, TelemetrySettings t)
    {
        DetailedLogs = l.DetailedLogs;
        AmbientDetails = l.AmbientDetails;
        TranscriptionDetails = l.TranscriptionDetails;
        AutocorrectDetails = l.AutocorrectDetails;
        InputDetails = l.InputDetails;
        WindowingDetails = l.WindowingDetails;
        LlmDetails = l.LlmDetails;
        ApplicationLogToDisk = l.ApplicationLogToDisk;
        MicrophoneLevel = t.MicrophoneTelemetry;
        TelemetryStorageDirectory = t.StorageDirectory;
    }

    private void PushLogging(string setting, object value)
    {
        if (_isSyncing) return;
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
        MicrophoneLevel = t.MicrophoneTelemetry;
        ApplicationLogToDisk = l.ApplicationLogToDisk;
    }

    public void ResetFoldersDefaults()
        => TelemetryStorageDirectory = new TelemetrySettings().StorageDirectory;
}
