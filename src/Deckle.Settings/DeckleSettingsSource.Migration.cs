using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Settings;

public sealed partial class DeckleSettingsSource
{
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

}
