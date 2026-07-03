using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: the settings-UX events every settings page emits,
// wherever that page lives. A setting changed, a section reset, a folder picker
// that failed — these are the same observation whichever module owns the page, so
// per the "one source per kind, no variants" doctrine they land on ONE provider
// rather than being duplicated into each module's own source. It lives in
// Deckle.Diagnostics — the logging floor every module already references — so a
// page relocated into its module (WhisperPage, RecordingPage, DiagnosticsPage…)
// logs its setting-changes here without a reference back to the Settings shell.
//
// This is deliberately NARROW: only the generic, page-agnostic settings events
// belong here. The Settings shell keeps its own Deckle-Settings provider for what
// is genuinely shell-scoped — navigation, backup, legacy migration, the module
// nav registry — none of which a module page emits.
[EventSource(Name = "Deckle-SettingsUx")]
public sealed class DeckleSettingsUxSource : DeckleEventSource
{
    public static readonly DeckleSettingsUxSource Log = new();

    private DeckleSettingsUxSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtSettingChanged         = 1;
    public const int EvtSectionReset           = 2;
    public const int EvtSectionResetDetail     = 3;
    public const int EvtFolderPickerFailed     = 4;
    public const int EvtFolderPickerFailedDetail = 5;

    // ── Setting change ──────────────────────────────────────────────────
    //
    // Property setters in settings ViewModels follow a homogeneous "a setting
    // changed" pattern. One parameterized SettingChanged(setting, value) Verbose
    // event carries them all — the setting name and value as structured fields —
    // rather than one typed event per setting with no semantic gain. Per-setting
    // changes are diagnostic detail, hence Verbose.
    [Event(EvtSettingChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setting changed | setting={0} | value={1}")]
    public void SettingChanged(string setting, string value)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSettingChanged, setting, value);
    }

    // ── Section reset ───────────────────────────────────────────────────
    //
    // A deliberate, rare action — an Info milestone with a Verbose mirror carrying
    // the section name.
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

    // ── Folder picker ───────────────────────────────────────────────────
    //
    // The shared FolderPickerCard / FolderPickerEditableCard, and any page opening
    // a folder in Explorer, report a failed pick here — an Error milestone with a
    // Verbose mirror carrying the exception type and message.
    [Event(EvtFolderPickerFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The folder picker failed")]
    public void FolderPickerFailed()
    {
        if (IsEnabled()) WriteEvent(EvtFolderPickerFailed);
    }

    [Event(EvtFolderPickerFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "folder picker failed | error={0} | message={1}")]
    public void FolderPickerFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtFolderPickerFailedDetail, ex_type, message);
    }
}
