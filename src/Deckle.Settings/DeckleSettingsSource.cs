using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// Settings module provider: legacy → per-module migration (SettingsBootstrap),
// the SettingsWindow shell and General page, the backup service and the shell
// settings persistence (SettingsService).
//
// An Info / Warning / Error is a short Capital sentence with no IDs, paths, or
// k=v; the technical detail (ids, paths, exception type+message, page/tag
// names) lives in a Verbose mirror that FOLLOWS it. Event ids are public in the
// ETW manifest and are never reused.
[EventSource(Name = "Deckle-Settings")]
public sealed partial class DeckleSettingsSource : DeckleEventSource
{
    public static readonly DeckleSettingsSource Log = new();

    // Restarted once per navigation in OnNavSelectionChanged; NavTiming reads it.
    // Static so a destination page, which has no window reference, can time its
    // first Loaded against it (PageReady). Single-window UI-thread nav, so no
    // contention.
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

    // ── Folder picker ──
    // 23 is burned: the folder-picker events live in DeckleSettingsUxSource.
    // Never reuse.

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

    // ── ViewModels (generic) ──
    // 40, 41 and 42 are burned: SettingChanged and SectionReset live in the
    // shared DeckleSettingsUxSource, so a module page emits them without a
    // reference to the shell. Never reuse.

    // ── Settings persistence (transitional) ──
    public const int EvtSettingsLoaded                    = 43;
    public const int EvtSettingsLoadComplete              = 44;
    public const int EvtSettingsLoadWarning               = 45;
    public const int EvtSettingsLoadError                 = 46;

    // ── Verbose mirrors ──
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
    // 60 is burned (folder picker, DeckleSettingsUxSource). Never reuse.
    public const int EvtSetupWindowOpenFailedDetail       = 61;
    public const int EvtWarmupRestartFailedDetail         = 62;
    public const int EvtNavImpossibleNoTagDetail          = 63;
    public const int EvtNavFailedTypeNotFoundDetail       = 64;
    public const int EvtNavStartedDetail                  = 65;
    public const int EvtNavFailedFrameRejectedDetail      = 66;
    public const int EvtNavCompletedDetail                = 67;
    public const int EvtNavFailedThrewDetail              = 68;
    // 69 is burned (section reset, DeckleSettingsUxSource). Never reuse.

    // ── Page navigation timing (ms) ──
    // Numbers ⇒ Verbose only.
    public const int EvtNavTiming                         = 70;
    public const int EvtPageReady                         = 71;

    // ── Settings module nav registry ──
    // Plumbing detail ⇒ Verbose; a tag that fails to resolve surfaces through
    // the NavFailedTypeNotFound milestone when the item is selected.
    public const int EvtSettingsModuleRegistered          = 72;
    public const int EvtSettingsModuleUnregistered        = 73;

    // ── Settings cross-page search index ──
    // A search entry whose header key does not resolve when the index is built,
    // skipped so the rest of the page still indexes. Verbose, no user milestone.
    public const int EvtSearchEntrySkipped                = 74;

    // ── Settings cross-page search (TitleBar box) ───────────────────────
    // SearchExecuted logs the query length, never the text (privacy), and fires
    // per settled keystroke ⇒ Verbose. Picking a hit is a deliberate user action
    // ⇒ Info milestone, its page/card tags in the Verbose mirror.
    public const int EvtSearchExecuted                    = 75;
    public const int EvtSearchNavigated                   = 76;
    public const int EvtSearchNavigatedDetail             = 77;

    // ── Settings TitleBar layout & search presentation ──
    // The observables that let a layout defect at an unseen window size be read
    // back from a trace. Widths in DIPs ⇒ Verbose throughout.
    public const int EvtTitleBarLayout                    = 78;
    public const int EvtSearchPresentationChanged         = 79;
    public const int EvtSearchFocusReleased               = 80;

    // ── Settings module nav nesting ──
    // A child whose ParentId names no registered module gets no rail item; the
    // rest of the band still builds. Verbose.
    public const int EvtSettingsModuleParentMissing       = 81;

}
