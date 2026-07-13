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
public sealed class DeckleSettingsSource : DeckleEventSource
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

    // ── Bootstrap ───────────────────────────────────────────────────────

    [Event(EvtMigrationDispatched,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A settings section was migrated to its module file")]
    public void MigrationDispatched()
    {
        if (IsEnabled()) WriteEvent(EvtMigrationDispatched);
    }

    [Event(EvtMigrationDispatchedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "migration dispatched | json_key={0} | module_id={1}")]
    public void MigrationDispatchedDetail(string json_key, string module_id)
    {
        if (IsEnabled()) WriteEvent(EvtMigrationDispatchedDetail, json_key, module_id);
    }

    [Event(EvtMigrationDispatchSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "dispatch {0} skipped (target exists) | path={1}")]
    public void MigrationDispatchSkipped(string json_key, string target_path)
    {
        if (IsEnabled()) WriteEvent(EvtMigrationDispatchSkipped, json_key, target_path);
    }

    // Constant migration (paths.modelsDirectory → modules/whisp/modelsDirectory,
    // both compile-time keys) documented here; the milestone carries no detail,
    // so no Verbose mirror is needed.
    [Event(EvtMigrationModelsDirectoryDispatched,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The models directory setting was migrated")]
    public void MigrationModelsDirectoryDispatched()
    {
        if (IsEnabled()) WriteEvent(EvtMigrationModelsDirectoryDispatched);
    }

    [Event(EvtSettingsSplitIntoPerModuleFiles,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Settings split into per-module files")]
    public void SettingsSplitIntoPerModuleFiles()
    {
        if (IsEnabled()) WriteEvent(EvtSettingsSplitIntoPerModuleFiles);
    }

    [Event(EvtPerModuleMigrationFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Per-module migration failed")]
    public void PerModuleMigrationFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPerModuleMigrationFailed);
    }

    [Event(EvtPerModuleMigrationFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "per-module migration failed | error={0} | message={1}")]
    public void PerModuleMigrationFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtPerModuleMigrationFailedDetail, ex_type, message);
    }

    [Event(EvtInjectFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not inject a key into a module file")]
    public void InjectFailed()
    {
        if (IsEnabled()) WriteEvent(EvtInjectFailed);
    }

    [Event(EvtInjectFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "inject failed | key={0} | module_id={1} | error={2} | message={3}")]
    public void InjectFailedDetail(string key, string module_id, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtInjectFailedDetail, key, module_id, ex_type, message);
    }

    [Event(EvtModuleFolderMigrated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Module folder migrated")]
    public void ModuleFolderMigrated()
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderMigrated);
    }

    [Event(EvtModuleFolderMigratedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "module folder migrated | old_id={0} | new_id={1}")]
    public void ModuleFolderMigratedDetail(string old_id, string new_id)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderMigratedDetail, old_id, new_id);
    }

    [Event(EvtModuleRenameDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "module rename | old=modules/{0} | new=modules/{1}")]
    public void ModuleRenameDetail(string old_id, string new_id)
    {
        if (IsEnabled()) WriteEvent(EvtModuleRenameDetail, old_id, new_id);
    }

    [Event(EvtModuleFolderRenameFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not rename a module folder")]
    public void ModuleFolderRenameFailed()
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderRenameFailed);
    }

    [Event(EvtModuleFolderRenameFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "module rename failed | old=modules/{0} | new=modules/{1} | error={2}: {3}")]
    public void ModuleFolderRenameFailedDetail(string old_id, string new_id, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderRenameFailedDetail, old_id, new_id, ex_type, message);
    }

    // Mirror for the reworded ModuleFolderRenameFailed milestone. A `…Detail`
    // (id 10) already carried the full error tuple; this `…Detail2` carries the
    // ids alone so the milestone and its mirror stay a clean pair. Both keep the
    // same Keywords as the milestone.
    [Event(EvtModuleFolderRenameFailedDetail2,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "module rename failed | old=modules/{0} | new=modules/{1}")]
    public void ModuleFolderRenameFailedDetail2(string old_id, string new_id)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderRenameFailedDetail2, old_id, new_id);
    }

    [Event(EvtRenamedRootKey,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A settings key was renamed")]
    public void RenamedRootKey()
    {
        if (IsEnabled()) WriteEvent(EvtRenamedRootKey);
    }

    [Event(EvtRenamedRootKeyDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "root key renamed | old_key={0} | new_key={1}")]
    public void RenamedRootKeyDetail(string old_key, string new_key)
    {
        if (IsEnabled()) WriteEvent(EvtRenamedRootKeyDetail, old_key, new_key);
    }

    // Constant migration (corpusLogging → telemetry, both compile-time keys)
    // documented here; the milestone carries no detail, no Verbose mirror.
    [Event(EvtMigratedCorpusToTelemetry,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Corpus logging was migrated to telemetry")]
    public void MigratedCorpusToTelemetry()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedCorpusToTelemetry);
    }

    // Constant migration (llm.manualProfileName → llm.slotAProfileName, both
    // compile-time keys); milestone carries no detail, no Verbose mirror.
    [Event(EvtMigratedLlmManualToSlotA,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An LLM profile key was migrated")]
    public void MigratedLlmManualToSlotA()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedLlmManualToSlotA);
    }

    // Constant migration (llm.slotAProfileName → llm.primaryRewriteProfileName,
    // both compile-time keys); milestone carries no detail, no Verbose mirror.
    [Event(EvtMigratedLlmSlotAToPrimary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The primary LLM profile key was migrated")]
    public void MigratedLlmSlotAToPrimary()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedLlmSlotAToPrimary);
    }

    // Constant migration (llm.slotBProfileName → llm.secondaryRewriteProfileName,
    // both compile-time keys); milestone carries no detail, no Verbose mirror.
    [Event(EvtMigratedLlmSlotBToSecondary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The secondary LLM profile key was migrated")]
    public void MigratedLlmSlotBToSecondary()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedLlmSlotBToSecondary);
    }

    // ── Backup ──────────────────────────────────────────────────────────

    [Event(EvtBackupSkippedSourceMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Backup skipped because the source file is missing")]
    public void BackupSkippedSourceMissing()
    {
        if (IsEnabled()) WriteEvent(EvtBackupSkippedSourceMissing);
    }

    [Event(EvtBackupSkippedSourceMissingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup skipped — source missing | path={0}")]
    public void BackupSkippedSourceMissingDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtBackupSkippedSourceMissingDetail, path);
    }

    [Event(EvtBackupCreated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Backup created")]
    public void BackupCreated()
    {
        if (IsEnabled()) WriteEvent(EvtBackupCreated);
    }

    [Event(EvtBackupCreatedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup created | path={0}")]
    public void BackupCreatedDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtBackupCreatedDetail, path);
    }

    [Event(EvtBackupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Backup failed")]
    public void BackupFailed()
    {
        if (IsEnabled()) WriteEvent(EvtBackupFailed);
    }

    [Event(EvtBackupFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup failed | error={0} | message={1}")]
    public void BackupFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtBackupFailedDetail, ex_type, message);
    }

    [Event(EvtBackupListFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not list the backups")]
    public void BackupListFailed()
    {
        if (IsEnabled()) WriteEvent(EvtBackupListFailed);
    }

    [Event(EvtBackupListFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup list failed | error={0} | message={1}")]
    public void BackupListFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtBackupListFailedDetail, ex_type, message);
    }

    [Event(EvtRestoreSkippedSnapshotMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Restore skipped because the snapshot is missing")]
    public void RestoreSkippedSnapshotMissing()
    {
        if (IsEnabled()) WriteEvent(EvtRestoreSkippedSnapshotMissing);
    }

    [Event(EvtRestoreSkippedSnapshotMissingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restore skipped — snapshot missing | path={0}")]
    public void RestoreSkippedSnapshotMissingDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtRestoreSkippedSnapshotMissingDetail, path);
    }

    [Event(EvtRestoredFromBackup,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Restored from backup")]
    public void RestoredFromBackup()
    {
        if (IsEnabled()) WriteEvent(EvtRestoredFromBackup);
    }

    [Event(EvtRestoredFromBackupDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restored from backup | path={0}")]
    public void RestoredFromBackupDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtRestoredFromBackupDetail, path);
    }

    [Event(EvtRestoreFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Restore failed")]
    public void RestoreFailed()
    {
        if (IsEnabled()) WriteEvent(EvtRestoreFailed);
    }

    [Event(EvtRestoreFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restore failed | error={0} | message={1}")]
    public void RestoreFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtRestoreFailedDetail, ex_type, message);
    }

    // ── General page (setup wizard) ─────────────────────────────────────

    // Pure status sentence, no params; cleaned and recapitalized in place.
    [Event(EvtSetupWizardHookNotWired,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup wizard hook is not wired")]
    public void SetupWizardHookNotWired()
    {
        if (IsEnabled()) WriteEvent(EvtSetupWizardHookNotWired);
    }

    // Pure status sentence, no params; cleaned and recapitalized in place.
    [Event(EvtSetupWindowOpenedFromSettings,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Setup window opened from Settings")]
    public void SetupWindowOpenedFromSettings()
    {
        if (IsEnabled()) WriteEvent(EvtSetupWindowOpenedFromSettings);
    }

    [Event(EvtSetupWindowOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not open the setup window")]
    public void SetupWindowOpenFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSetupWindowOpenFailed);
    }

    [Event(EvtSetupWindowOpenFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setup window open failed | error={0} | message={1}")]
    public void SetupWindowOpenFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtSetupWindowOpenFailedDetail, ex_type, message);
    }

    [Event(EvtWarmupRestartFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup restart failed")]
    public void WarmupRestartFailed()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupRestartFailed);
    }

    [Event(EvtWarmupRestartFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup restart failed | error={0} | message={1}")]
    public void WarmupRestartFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupRestartFailedDetail, ex_type, message);
    }

    // ── SettingsWindow navigation ───────────────────────────────────────

    [Event(EvtNavSelectionChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "selection changed | item={0}")]
    public void NavSelectionChanged(string item_content)
    {
        if (IsEnabled()) WriteEvent(EvtNavSelectionChanged, item_content);
    }

    [Event(EvtNavSelectionIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "selection ignored | reason={0}")]
    public void NavSelectionIgnored(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtNavSelectionIgnored, reason);
    }

    [Event(EvtNavImpossibleNoTag,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A navigation item has no destination tag")]
    public void NavImpossibleNoTag()
    {
        if (IsEnabled()) WriteEvent(EvtNavImpossibleNoTag);
    }

    [Event(EvtNavImpossibleNoTagDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav impossible | reason=no-tag | item={0}")]
    public void NavImpossibleNoTagDetail(string item_content)
    {
        if (IsEnabled()) WriteEvent(EvtNavImpossibleNoTagDetail, item_content);
    }

    [Event(EvtNavFailedTypeNotFound,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A navigation target page type was not found")]
    public void NavFailedTypeNotFound()
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedTypeNotFound);
    }

    [Event(EvtNavFailedTypeNotFoundDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav failed | reason=type-not-found | tag={0}")]
    public void NavFailedTypeNotFoundDetail(string tag)
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedTypeNotFoundDetail, tag);
    }

    [Event(EvtNavSkippedAlreadyCurrent,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav skipped | reason=already-current | page={0}")]
    public void NavSkippedAlreadyCurrent(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavSkippedAlreadyCurrent, page_name);
    }

    [Event(EvtNavStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigation started")]
    public void NavStarted()
    {
        if (IsEnabled()) WriteEvent(EvtNavStarted);
    }

    [Event(EvtNavStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "navigation started | page={0}")]
    public void NavStartedDetail(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavStartedDetail, page_name);
    }

    [Event(EvtNavFailedFrameRejected,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigation was rejected by the frame")]
    public void NavFailedFrameRejected()
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedFrameRejected);
    }

    [Event(EvtNavFailedFrameRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "navigate failed | page={0} | reason=frame-returned-false")]
    public void NavFailedFrameRejectedDetail(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedFrameRejectedDetail, page_name);
    }

    [Event(EvtNavCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigation completed")]
    public void NavCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtNavCompleted);
    }

    [Event(EvtNavCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "navigation completed | page={0}")]
    public void NavCompletedDetail(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavCompletedDetail, page_name);
    }

    // (a) Navigate-return duration, from NavClock. Mirrors whisper's
    // ModelLoadComplete(load_ms, backend): a measured ms as a typed field on a
    // Verbose event. Pairs with the NavStarted milestone above.
    [Event(EvtNavTiming,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav timing | page={0} | duration_ms={1}")]
    public void NavTiming(string page_name, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtNavTiming, page_name, duration_ms);
    }

    // (b) Time from nav-start (NavClock) to the destination page's first
    // Loaded — captures the heavy work (ViewModel.Load + control sync) that
    // Navigate returns BEFORE. Verbose, ms.
    [Event(EvtPageReady,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page ready | page={0} | ready_ms={1}")]
    public void PageReady(string page_name, long ready_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPageReady, page_name, ready_ms);
    }

    [Event(EvtNavFailedThrew,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigation threw an exception")]
    public void NavFailedThrew()
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedThrew);
    }

    [Event(EvtNavFailedThrewDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "navigate threw | page={0} | error={1}: {2}")]
    public void NavFailedThrewDetail(string page_name, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedThrewDetail, page_name, ex_type, message);
    }

    // Demoted from Error to Verbose: a raw stack trace is opaque internal
    // detail with no standalone milestone value. It follows the NavFailedThrew
    // milestone (and its …Detail mirror) as the deep-dive line.
    [Event(EvtNavStackTrace,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav stack trace | stack={0}")]
    public void NavStackTrace(string stack)
    {
        if (IsEnabled()) WriteEvent(EvtNavStackTrace, stack);
    }

    [Event(EvtItemInvoked,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item invoked | content={0} | tag={1}")]
    public void ItemInvoked(string item_content, string item_tag)
    {
        if (IsEnabled()) WriteEvent(EvtItemInvoked, item_content, item_tag);
    }

    [Event(EvtOpenLogsFromFooter,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Open logs from footer")]
    public void OpenLogsFromFooter()
    {
        if (IsEnabled()) WriteEvent(EvtOpenLogsFromFooter);
    }

    // ── Settings persistence (transitoire) ──────────────────────────────

    [Event(EvtSettingsLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoaded(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoaded, message);
    }

    [Event(EvtSettingsLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadComplete(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadComplete, message);
    }

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    // ── Settings module nav registry ────────────────────────────────────

    [Event(EvtSettingsModuleRegistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "settings module registered | id={0} | tag={1}")]
    public void SettingsModuleRegistered(string id, string tag)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsModuleRegistered, id, tag);
    }

    [Event(EvtSettingsModuleUnregistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "settings module unregistered | id={0}")]
    public void SettingsModuleUnregistered(string id)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsModuleUnregistered, id);
    }

    // ── Settings cross-page search index ────────────────────────────────

    [Event(EvtSearchEntrySkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "search entry skipped | reason=header-unresolved | page={0} | key={1}")]
    public void SearchEntrySkipped(string page_tag, string label_key)
    {
        if (IsEnabled()) WriteEvent(EvtSearchEntrySkipped, page_tag, label_key);
    }

    // ── Settings cross-page search (TitleBar box) ───────────────────────

    [Event(EvtSearchExecuted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "search executed | query_len={0} | hits={1}")]
    public void SearchExecuted(int query_len, int hits)
    {
        if (IsEnabled()) WriteEvent(EvtSearchExecuted, query_len, hits);
    }

    [Event(EvtSearchNavigated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigated from search")]
    public void SearchNavigated()
    {
        if (IsEnabled()) WriteEvent(EvtSearchNavigated);
    }

    [Event(EvtSearchNavigatedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "search navigation | page={0} | card={1}")]
    public void SearchNavigatedDetail(string page_tag, string card_tag)
    {
        if (IsEnabled()) WriteEvent(EvtSearchNavigatedDetail, page_tag, card_tag);
    }
}
