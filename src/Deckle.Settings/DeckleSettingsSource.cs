using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// Settings module provider. Covers legacy → per-module migration
// (SettingsBootstrap), the Settings UI surface (SettingsWindow, General /
// Diagnostics / Recording pages, consent dialogs), ViewModels
// (GeneralViewModel, RecordingViewModel, DiagnosticsViewModel), the backup
// service (SettingsBackupService), folder pickers (FolderPickerCard /
// FolderPickerEditableCard), and global-module settings persistence
// (SettingsService).
//
// For the ViewModel setter area, each setter logs the setting it changed
// through a single parameterized SettingChanged(setting, value) Verbose event —
// one event for the "a setting changed" operation, the setting name and value
// as structured fields, rather than forty typed events with no semantic gain.
// Per-setting changes are diagnostic detail and sit at Verbose; a section reset
// is a deliberate, rare action and keeps an Info milestone with a Verbose
// mirror.
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info / Warning /
// Error is a short Capital sentence with no IDs, paths, or k=v; the technical
// detail (ids, paths, exception type+message, page/tag names) lives in a
// Verbose mirror that FOLLOWS it. The mirrors added for that separation take
// fresh ids 47-68 at the end of the id block; existing ids are public in the
// ETW manifest and are never reused.
[EventSource(Name = "Deckle-Settings")]
public sealed partial class DeckleSettingsSource : DeckleEventSource
{
    public static readonly DeckleSettingsSource Log = new();

    // Single nav-start clock for the SettingsWindow. Restarted in
    // OnNavSelectionChanged at nav-start; the Navigate-return elapsed feeds
    // NavTiming (a) and the destination page's first Loaded feeds PageReady (b),
    // so both measures share ONE timestamp set once per navigation. Static
    // because the page's Loaded handler has no window reference but does see the
    // provider. Single-window, single-threaded UI nav — no contention.
    public static readonly Stopwatch NavClock = new();

    private DeckleSettingsSource() { }

    // ── Bootstrap (legacy → per-module migration) ──
    public const int EvtMigrationDispatched               = 1;
    public const int EvtMigrationDispatchSkipped          = 2;
    public const int EvtMigrationModelsDirectoryDispatched = 3;
    public const int EvtSettingsSplitIntoPerModuleFiles   = 4;
    public const int EvtPerModuleMigrationFailed          = 5;
    public const int EvtInjectFailed                      = 6;
    public const int EvtModuleFolderMigrated              = 7;
    public const int EvtModuleRenameDetail                = 8;
    public const int EvtModuleFolderRenameFailed          = 9;
    public const int EvtModuleFolderRenameFailedDetail    = 10;
    public const int EvtRenamedRootKey                    = 11;
    public const int EvtMigratedCorpusToTelemetry         = 12;
    public const int EvtMigratedLlmManualToSlotA          = 13;
    public const int EvtMigratedLlmSlotAToPrimary         = 14;
    public const int EvtMigratedLlmSlotBToSecondary       = 15;

    // ── Backup ──
    public const int EvtBackupSkippedSourceMissing        = 16;
    public const int EvtBackupCreated                     = 17;
    public const int EvtBackupFailed                      = 18;
    public const int EvtBackupListFailed                  = 19;
    public const int EvtRestoreSkippedSnapshotMissing     = 20;
    public const int EvtRestoredFromBackup                = 21;
    public const int EvtRestoreFailed                     = 22;

    // ── Folder picker errors — MOVED to Deckle-SettingsUx ──
    // FolderPickerFailed (23) and its detail (60) moved to DeckleSettingsUxSource,
    // the shared settings-UX provider. IDs burned here, never reused.

    // ── General page (setup wizard) ──
    public const int EvtSetupWizardHookNotWired           = 24;
    public const int EvtSetupWindowOpenedFromSettings     = 25;
    public const int EvtSetupWindowOpenFailed             = 26;
    public const int EvtWarmupRestartFailed               = 27;

    // ── SettingsWindow navigation ──
    public const int EvtNavSelectionChanged               = 28;
    public const int EvtNavSelectionIgnored               = 29;
    public const int EvtNavImpossibleNoTag                = 30;
    public const int EvtNavFailedTypeNotFound             = 31;
    public const int EvtNavSkippedAlreadyCurrent          = 32;
    public const int EvtNavStarted                        = 33;
    public const int EvtNavFailedFrameRejected            = 34;
    public const int EvtNavCompleted                      = 35;
    public const int EvtNavFailedThrew                    = 36;
    public const int EvtNavStackTrace                     = 37;
    public const int EvtItemInvoked                       = 38;
    public const int EvtOpenLogsFromFooter                = 39;

    // ── ViewModels (generic) — MOVED to Deckle-SettingsUx ──
    // SettingChanged (40), SectionReset (42) and SectionResetDetail (69) moved to
    // the shared DeckleSettingsUxSource so a relocated module page emits them
    // without a back-reference to the shell; 41 was already burned. Never reused.

    // ── Settings persistence (transitoire) ──
    public const int EvtSettingsLoaded                    = 43;
    public const int EvtSettingsLoadComplete              = 44;
    public const int EvtSettingsLoadWarning               = 45;
    public const int EvtSettingsLoadError                 = 46;

    // ── Verbose mirrors appended for the Verbose/Info separation ──
    // Each milestone above whose message carried an id / path / k=v / exception
    // detail now emits a short Capital sentence, and the technical detail moves
    // to one of these fresh ids. IDs are public in the ETW manifest; never
    // reuse an id. ModuleFolderRenameFailed already had a `…Detail` (id 10,
    // ids + error), so its new ids-only mirror is `…Detail2` (id 51).
    public const int EvtMigrationDispatchedDetail         = 47;
    public const int EvtPerModuleMigrationFailedDetail    = 48;
    public const int EvtInjectFailedDetail                = 49;
    public const int EvtModuleFolderMigratedDetail        = 50;
    public const int EvtModuleFolderRenameFailedDetail2   = 51;
    public const int EvtRenamedRootKeyDetail              = 52;
    public const int EvtBackupSkippedSourceMissingDetail  = 53;
    public const int EvtBackupCreatedDetail               = 54;
    public const int EvtBackupFailedDetail                = 55;
    public const int EvtBackupListFailedDetail            = 56;
    public const int EvtRestoreSkippedSnapshotMissingDetail = 57;
    public const int EvtRestoredFromBackupDetail          = 58;
    public const int EvtRestoreFailedDetail               = 59;
    // 60 — EvtFolderPickerFailedDetail moved to Deckle-SettingsUx. Burned, never reused.
    public const int EvtSetupWindowOpenFailedDetail       = 61;
    public const int EvtWarmupRestartFailedDetail         = 62;
    public const int EvtNavImpossibleNoTagDetail          = 63;
    public const int EvtNavFailedTypeNotFoundDetail       = 64;
    public const int EvtNavStartedDetail                  = 65;
    public const int EvtNavFailedFrameRejectedDetail      = 66;
    public const int EvtNavCompletedDetail                = 67;
    public const int EvtNavFailedThrewDetail              = 68;
    // 69 — EvtSectionResetDetail moved to Deckle-SettingsUx. Burned, never reused.

    // ── Page navigation timing (structured-verbose, ms) ──
    // Paired with the existing NavStarted milestone: NavTiming carries the
    // Navigate-return wall time, PageReady the time to the page's first Loaded
    // — both from NavClock (set once per nav). Numbers ⇒ Verbose only.
    public const int EvtNavTiming                         = 70;
    public const int EvtPageReady                         = 71;

    // ── Settings module nav registry ──
    // A module contributing / withdrawing its settings page in the shell's
    // NavigationView (SettingsModuleRegistry). Plumbing detail with an id and a
    // tag ⇒ Verbose; a resolution failure of the tag surfaces on its own through
    // the NavFailedTypeNotFound milestone when the item is selected.
    public const int EvtSettingsModuleRegistered          = 72;
    public const int EvtSettingsModuleUnregistered        = 73;

    // ── Settings cross-page search index ──
    // A search entry whose header key does not resolve when the index is built —
    // a dangling contribution, skipped so the rest of the page still indexes.
    // Plumbing detail with a tag and a key ⇒ Verbose (no user milestone).
    public const int EvtSearchEntrySkipped                = 74;

    // ── Settings cross-page search (TitleBar box) ───────────────────────
    // A debounced query ran: query length (not the text — privacy) and hit count
    // are measures ⇒ Verbose, no milestone (it fires per settled keystroke).
    // Picking a hit is a deliberate user action ⇒ Info milestone with no ids, its
    // page/card tags in the Verbose mirror that follows.
    public const int EvtSearchExecuted                    = 75;
    public const int EvtSearchNavigated                   = 76;
    public const int EvtSearchNavigatedDetail             = 77;

    // ── Settings TitleBar layout & search presentation ──
    // Geometry snapshot (debounced) after a resize or a presentation swap, plus
    // the presentation transitions and explicit focus releases themselves — the
    // observables that let a layout defect at an unseen window size be read back
    // from a trace. Diagnostic detail, widths in DIPs ⇒ Verbose throughout.
    public const int EvtTitleBarLayout                    = 78;
    public const int EvtSearchPresentationChanged         = 79;
    public const int EvtSearchFocusReleased               = 80;

}
