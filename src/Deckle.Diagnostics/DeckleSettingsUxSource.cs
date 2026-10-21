using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: the settings-UX events every settings page emits,
// wherever that page lives. A setting changed, a section reset, a folder picker
// that failed — these are the same observation whichever module owns the page, so
// they land on one provider rather than a copy in each module's own source.
// Deckle.Diagnostics is referenced by every module, so a module's own page logs
// here without a reference back to the Settings shell.
//
// Only page-agnostic settings events belong here. The Settings shell keeps its
// own Deckle-Settings provider for shell-scoped events — navigation, backup,
// legacy migration, the module nav registry.
[EventSource(Name = "Deckle-SettingsUx")]
public sealed class DeckleSettingsUxSource : DeckleEventSource
{
    public static readonly DeckleSettingsUxSource Log = new();

    private DeckleSettingsUxSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest; never reuse an id after deleting an event.
    public const int EvtSettingChanged         = 1;
    public const int EvtSectionReset           = 2;
    public const int EvtSectionResetDetail     = 3;
    public const int EvtFolderPickerFailed     = 4;
    public const int EvtFolderPickerFailedDetail = 5;
    public const int EvtFolderActionFailed     = 6;

    // ── Setting change ──────────────────────────────────────────────────
    //
    // One parameterized event carries every settings-ViewModel setter, rather
    // than one typed event per setting with no semantic gain. The value is
    // redacted here, not at the call site: a new setter that forgets to scrub
    // a folder path or a device name still logs "(changed)", see
    // SettingValueRedaction.
    [Event(EvtSettingChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setting changed | setting={0} | value={1}")]
    public void SettingChanged(string setting, string value)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSettingChanged, setting, SettingValueRedaction.Redact(setting, value));
    }

    // ── Section reset ───────────────────────────────────────────────────
    [Event(EvtSectionReset,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A settings section was reset to defaults")]
    public void SectionReset()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSectionReset);
    }

    [Event(EvtSectionResetDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "section reset | section={0}")]
    public void SectionResetDetail(string section)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSectionResetDetail, section);
    }

    // ── Folder actions ──────────────────────────────────────────────────
    //
    // Kept until the call sites outside this module move to FolderActionFailed.
    // The pair says neither which folder action failed nor why in the
    // milestone itself, and its detail carries the raw exception message,
    // which can hold the person's profile path.
    [Event(EvtFolderPickerFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The folder picker failed")]
    public void FolderPickerFailed()
    {
        if (IsEnabled()) WriteEvent(EvtFolderPickerFailed);
    }

    // Kept until the call sites outside this module move to FolderActionFailed.
    [Event(EvtFolderPickerFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "folder picker failed | error={0} | message={1}")]
    public void FolderPickerFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtFolderPickerFailedDetail, ex_type, message);
    }

    // A folder action the person started from a settings page failed; the
    // page stays usable, the action did not happen. Reported by the shared
    // folder location cards and by any page that opens or moves a folder.
    // Closed `action` vocabulary:
    //   "pick"     — the folder picker dialog
    //   "open"     — opening the folder in Explorer
    //   "relocate" — moving a data folder to the picked location
    // `ex_message` is scrubbed of the user profile root before it is written,
    // since IO exceptions quote the path they failed on.
    [Event(EvtFolderActionFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A folder action failed | action={0} | ex_type={1} | hr=0x{2:X8} | ex_message={3}")]
    public void FolderActionFailed(string action, string ex_type, int hr, string ex_message)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtFolderActionFailed, action, ex_type, hr, ScrubUserProfile(ex_message));
    }

    internal static string ScrubUserProfile(string text)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile)
            ? text
            : text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }
}

// Decides which setting values may reach the log verbatim. Allowlist, not
// blocklist: a setting is safe because someone checked its value carries no
// personal data (a toggle, a number, an enum, a hotkey's modifier flags and
// scancode), so an unknown name falls back to "(changed)". Folder paths name
// the person (C:\Users\<name>\...) and stay out on purpose.
internal static class SettingValueRedaction
{
    internal const string Redacted = "(changed)";

    private static readonly HashSet<string> s_valueBearing = new(StringComparer.Ordinal)
    {
        "Theme",
        "Start with Windows",
        "Start elevated",
        "Updates.AutoCheckEnabled",
        // Device index, not the device name.
        "Audio input device",
        "MaxRecordingDurationSeconds",
        "Preprocessing.Enabled",
        "LevelWindow.MinDbfs",
        "LevelWindow.MaxDbfs",
        "LevelWindow.DbfsCurveExponent",
        "LevelWindow.AutoCalibration",
        "Telemetry.MicrophoneTelemetry",
        "Logging.LogAmbientCaptureActivity",
        "Logging.LogTranscriptionActivity",
        "Logging.LogAutocorrectActivity",
        "Logging.LogInputActivity",
        "Logging.LogWindowingActivity",
        "Logging.ApplicationLogToDisk",
        "Input.RecordWheelEvents",
    };

    // Hotkey setters format modifier flags and a scancode, never typed text.
    private const string HotkeyPrefix = "Hotkeys.";

    internal static string Redact(string setting, string value)
        => s_valueBearing.Contains(setting) || setting.StartsWith(HotkeyPrefix, StringComparison.Ordinal)
            ? value
            : Redacted;
}
