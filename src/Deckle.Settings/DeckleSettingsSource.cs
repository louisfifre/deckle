using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// Settings module provider. Couvre la migration legacy → per-module
// (SettingsBootstrap), la surface UI Settings (SettingsWindow, pages
// General / Diagnostics / Recording, dialogs de consentement), les
// ViewModels (GeneralViewModel, RecordingViewModel, DiagnosticsViewModel),
// le service de backup (SettingsBackupService), les pickers de dossier
// (FolderPickerCard / FolderPickerEditableCard), et la persistance
// settings du module global (SettingsService).
//
// Pour la zone setter des ViewModels — où chaque setter logue
// systématiquement "Property ← value" — un event paramétré générique
// SettingChanged(string property, string value) est privilégié sur
// l'expansion combinatoire en quarante events typés. Cette zone
// homogène par construction tolère le paramétrage générique sans
// dégrader la sémantique de strict-typed : le niveau et le keyword
// sont fixes, seuls les noms et valeurs de propriétés varient.
[EventSource(Name = "Deckle.Settings")]
public sealed class DeckleSettingsSource : DeckleEventSource
{
    public static readonly DeckleSettingsSource Log = new();

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

    // ── Folder picker errors ──
    public const int EvtFolderPickerFailed                = 23;

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

    // ── ViewModels (génériques) ──
    public const int EvtSettingChanged                    = 40;
    public const int EvtSettingChangedDetail              = 41;
    public const int EvtSectionReset                      = 42;

    // ── Settings persistence (transitoire) ──
    public const int EvtSettingsLoaded                    = 43;
    public const int EvtSettingsLoadComplete              = 44;
    public const int EvtSettingsLoadWarning               = 45;
    public const int EvtSettingsLoadError                 = 46;

    // ── Bootstrap ───────────────────────────────────────────────────────

    [Event(EvtMigrationDispatched,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "migrated {0} → modules/{1}/settings.json")]
    public void MigrationDispatched(string json_key, string module_id)
    {
        if (IsEnabled()) WriteEvent(EvtMigrationDispatched, json_key, module_id);
    }

    [Event(EvtMigrationDispatchSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "dispatch {0} skipped (target exists) | path={1}")]
    public void MigrationDispatchSkipped(string json_key, string target_path)
    {
        if (IsEnabled()) WriteEvent(EvtMigrationDispatchSkipped, json_key, target_path);
    }

    [Event(EvtMigrationModelsDirectoryDispatched,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "migrated paths.modelsDirectory → modules/whisp/modelsDirectory")]
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
           Message = "per-module migration failed: {0}: {1}")]
    public void PerModuleMigrationFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtPerModuleMigrationFailed, ex_type, message);
    }

    [Event(EvtInjectFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "inject {0} into {1} failed: {2}: {3}")]
    public void InjectFailed(string key, string module_id, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtInjectFailed, key, module_id, ex_type, message);
    }

    [Event(EvtModuleFolderMigrated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Module folder migrated ({0} → {1})")]
    public void ModuleFolderMigrated(string old_id, string new_id)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderMigrated, old_id, new_id);
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
           Message = "Module folder rename failed — {0} could not be moved to {1}.")]
    public void ModuleFolderRenameFailed(string old_id, string new_id)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderRenameFailed, old_id, new_id);
    }

    [Event(EvtModuleFolderRenameFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "module rename failed | old=modules/{0} | new=modules/{1} | error={2}: {3}")]
    public void ModuleFolderRenameFailedDetail(string old_id, string new_id, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderRenameFailedDetail, old_id, new_id, ex_type, message);
    }

    [Event(EvtRenamedRootKey,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "migrated {0} → {1}")]
    public void RenamedRootKey(string old_key, string new_key)
    {
        if (IsEnabled()) WriteEvent(EvtRenamedRootKey, old_key, new_key);
    }

    [Event(EvtMigratedCorpusToTelemetry,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "migrated corpusLogging → telemetry")]
    public void MigratedCorpusToTelemetry()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedCorpusToTelemetry);
    }

    [Event(EvtMigratedLlmManualToSlotA,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "migrated llm.manualProfileName → llm.slotAProfileName")]
    public void MigratedLlmManualToSlotA()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedLlmManualToSlotA);
    }

    [Event(EvtMigratedLlmSlotAToPrimary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "migrated llm.slotAProfileName → llm.primaryRewriteProfileName")]
    public void MigratedLlmSlotAToPrimary()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedLlmSlotAToPrimary);
    }

    [Event(EvtMigratedLlmSlotBToSecondary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "migrated llm.slotBProfileName → llm.secondaryRewriteProfileName")]
    public void MigratedLlmSlotBToSecondary()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedLlmSlotBToSecondary);
    }

    // ── Backup ──────────────────────────────────────────────────────────

    [Event(EvtBackupSkippedSourceMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup skipped — source missing | path={0}")]
    public void BackupSkippedSourceMissing(string path)
    {
        if (IsEnabled()) WriteEvent(EvtBackupSkippedSourceMissing, path);
    }

    [Event(EvtBackupCreated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup created | path={0}")]
    public void BackupCreated(string path)
    {
        if (IsEnabled()) WriteEvent(EvtBackupCreated, path);
    }

    [Event(EvtBackupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup failed | error={0}: {1}")]
    public void BackupFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtBackupFailed, ex_type, message);
    }

    [Event(EvtBackupListFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup list failed | error={0}: {1}")]
    public void BackupListFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtBackupListFailed, ex_type, message);
    }

    [Event(EvtRestoreSkippedSnapshotMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restore skipped — snapshot missing | path={0}")]
    public void RestoreSkippedSnapshotMissing(string path)
    {
        if (IsEnabled()) WriteEvent(EvtRestoreSkippedSnapshotMissing, path);
    }

    [Event(EvtRestoredFromBackup,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restored from backup | path={0}")]
    public void RestoredFromBackup(string path)
    {
        if (IsEnabled()) WriteEvent(EvtRestoredFromBackup, path);
    }

    [Event(EvtRestoreFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restore failed | error={0}: {1}")]
    public void RestoreFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtRestoreFailed, ex_type, message);
    }

    // ── Folder picker (FolderPickerCard + FolderPickerEditableCard) ─────

    [Event(EvtFolderPickerFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "FolderPicker failed: {0}: {1}")]
    public void FolderPickerFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtFolderPickerFailed, ex_type, message);
    }

    // ── General page (setup wizard) ─────────────────────────────────────

    [Event(EvtSetupWizardHookNotWired,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setup wizard hook not wired — ignoring")]
    public void SetupWizardHookNotWired()
    {
        if (IsEnabled()) WriteEvent(EvtSetupWizardHookNotWired);
    }

    [Event(EvtSetupWindowOpenedFromSettings,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setup window opened from Settings")]
    public void SetupWindowOpenedFromSettings()
    {
        if (IsEnabled()) WriteEvent(EvtSetupWindowOpenedFromSettings);
    }

    [Event(EvtSetupWindowOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setup window open failed: {0}: {1}")]
    public void SetupWindowOpenFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtSetupWindowOpenFailed, ex_type, message);
    }

    [Event(EvtWarmupRestartFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup restart failed: {0}: {1}")]
    public void WarmupRestartFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupRestartFailed, ex_type, message);
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
           Message = "nav impossible | reason=no-tag | item={0}")]
    public void NavImpossibleNoTag(string item_content)
    {
        if (IsEnabled()) WriteEvent(EvtNavImpossibleNoTag, item_content);
    }

    [Event(EvtNavFailedTypeNotFound,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "nav failed | reason=type-not-found | tag={0}")]
    public void NavFailedTypeNotFound(string tag)
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedTypeNotFound, tag);
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
           Message = "Navigate to {0}")]
    public void NavStarted(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavStarted, page_name);
    }

    [Event(EvtNavFailedFrameRejected,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "navigate failed | page={0} | reason=frame-returned-false")]
    public void NavFailedFrameRejected(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedFrameRejected, page_name);
    }

    [Event(EvtNavCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigated to {0}")]
    public void NavCompleted(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavCompleted, page_name);
    }

    [Event(EvtNavFailedThrew,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "navigate threw | page={0} | error={1}: {2}")]
    public void NavFailedThrew(string page_name, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedThrew, page_name, ex_type, message);
    }

    [Event(EvtNavStackTrace,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
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

    // ── ViewModels (générique paramétré) ────────────────────────────────
    //
    // Les setters de propriété dans les ViewModels suivent un pattern
    // homogène "Property ← value" — chaque setter logue son changement
    // à Info ou Verbose. La doctrine "strict-typed per opération" se
    // dégrade ici en une zone paramétrée stable : niveau et keyword
    // sont fixes, seuls (name, value) varient. Justifiable parce que
    // l'opération est elle-même générique par construction (un setter
    // qui logue), et que multiplier les events typés (forty Apparence-
    // ThemeChanged, OverlayEnabledChanged, etc.) sans gain sémantique
    // créerait du bruit sans bénéfice.

    [Event(EvtSettingChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0} ← {1}")]
    public void SettingChanged(string property, string value)
    {
        if (IsEnabled()) WriteEvent(EvtSettingChanged, property, value);
    }

    [Event(EvtSettingChangedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0} ← {1}")]
    public void SettingChangedDetail(string property, string value)
    {
        if (IsEnabled()) WriteEvent(EvtSettingChangedDetail, property, value);
    }

    [Event(EvtSectionReset,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0} section reset to defaults")]
    public void SectionReset(string section)
    {
        if (IsEnabled()) WriteEvent(EvtSectionReset, section);
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
}
