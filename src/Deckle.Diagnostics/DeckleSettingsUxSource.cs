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
    public const int EvtSettingChanged         = 1;
    public const int EvtSectionReset           = 2;
    public const int EvtSectionResetDetail     = 3;
    public const int EvtFolderPickerFailed     = 4;
    public const int EvtFolderPickerFailedDetail = 5;

    // ── Setting change ──────────────────────────────────────────────────
    //
    // One parameterized event carries every settings-ViewModel setter, rather
    // than one typed event per setting with no semantic gain.
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
    // Reported by the shared folder picker cards and by any page that opens a
    // folder in Explorer.
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
