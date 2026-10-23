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
           Message = "json_key={0} | module_id={1}")]
    public void MigrationDispatchedDetail(string json_key, string module_id)
    {
        if (IsEnabled()) WriteEvent(EvtMigrationDispatchedDetail, json_key, module_id);
    }

    [Event(EvtMigrationDispatchSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "json_key={0} | target_path={1}")]
    public void MigrationDispatchSkipped(string json_key, string target_path)
    {
        if (IsEnabled()) WriteEvent(EvtMigrationDispatchSkipped, json_key, target_path);
    }

    // Both keys are compile-time constants, so the milestone needs no Verbose
    // mirror.
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
           Message = "Splitting the settings into one file per module failed, so some module settings may read as defaults\nex_type={0} | ex_message={1}")]
    public void PerModuleMigrationFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPerModuleMigrationFailed, ex_type, ex_message);
    }

    // key and module_id are compile-time migration names, not user data.
    [Event(EvtInjectFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setting could not be moved into its module file, so it may read as its default\nkey={0} | module_id={1} | ex_type={2} | ex_message={3}")]
    public void InjectFailed(string key, string module_id, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtInjectFailed, key, module_id, ex_type, ex_message);
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
           Message = "old_id={0} | new_id={1}")]
    public void ModuleFolderMigratedDetail(string old_id, string new_id)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderMigratedDetail, old_id, new_id);
    }

    [Event(EvtModuleRenameDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "old=modules/{0} | new=modules/{1}")]
    public void ModuleRenameDetail(string old_id, string new_id)
    {
        if (IsEnabled()) WriteEvent(EvtModuleRenameDetail, old_id, new_id);
    }

    [Event(EvtModuleFolderRenameFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A module settings folder could not be renamed, so its settings may read as defaults\nold_id={0} | new_id={1} | ex_type={2} | ex_message={3}")]
    public void ModuleFolderRenameFailed(string old_id, string new_id, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFolderRenameFailed, old_id, new_id, ex_type, ex_message);
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
           Message = "old_key={0} | new_key={1}")]
    public void RenamedRootKeyDetail(string old_key, string new_key)
    {
        if (IsEnabled()) WriteEvent(EvtRenamedRootKeyDetail, old_key, new_key);
    }

    // Compile-time keys only, so no Verbose mirror.
    [Event(EvtMigratedCorpusToTelemetry,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Corpus logging was migrated to telemetry")]
    public void MigratedCorpusToTelemetry()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedCorpusToTelemetry);
    }

    // Compile-time keys only, so no Verbose mirror.
    [Event(EvtMigratedLlmManualToSlotA,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An LLM profile key was migrated")]
    public void MigratedLlmManualToSlotA()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedLlmManualToSlotA);
    }

    // Compile-time keys only, so no Verbose mirror.
    [Event(EvtMigratedLlmSlotAToPrimary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The primary LLM profile key was migrated")]
    public void MigratedLlmSlotAToPrimary()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedLlmSlotAToPrimary);
    }

    // Compile-time keys only, so no Verbose mirror.
    [Event(EvtMigratedLlmSlotBToSecondary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The secondary LLM profile key was migrated")]
    public void MigratedLlmSlotBToSecondary()
    {
        if (IsEnabled()) WriteEvent(EvtMigratedLlmSlotBToSecondary);
    }

}
