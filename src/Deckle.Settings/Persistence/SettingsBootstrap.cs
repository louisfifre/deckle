using System.Text.Json;
using System.Text.Json.Nodes;
using Deckle.Core;

namespace Deckle.Settings;

// ── SettingsBootstrap ─────────────────────────────────────────────────────
//
// One-shot migration step called at App startup, before any module
// SettingsService is constructed. Detects the legacy combined
// settings.json layout and dispatches each module section into its own
// modules/<id>/settings.json file.
//
// A separate static rather than a hook inside SettingsService.Load, because
// the migration must complete before any module's JsonSettingsStore opens its
// file: a store that finds no file writes its defaults, and the dispatch then
// skips that already-populated target and loses the legacy section.
//
// Idempotent: the dispatched sections are stripped from the legacy file, and
// a module file that already exists is never overwritten (it is the source of
// truth); its legacy section is dropped.
public static class SettingsBootstrap
{
    private static bool _migrated;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MigrateLegacyToPerModule()
    {
        if (_migrated) return;
        _migrated = true;

        // Before the legacy-file check, so a user already on the per-module
        // layout gets the folder relocated even without a legacy file.
        MigrateModuleFolder("capture", "audio");

        // The application-log consent belongs to Logging, not Telemetry. Move
        // it before either settings singleton opens its file; otherwise
        // Logging loads its off-by-default value and forgets the user's choice.
        MoveModuleSetting("telemetry", "logging", "applicationLogToDisk");
        RenameModuleSetting(
            "logging",
            "logStreamingTranscriptionActivity",
            "logTranscriptionActivity");

        string legacyPath = AppPaths.SettingsFilePath;
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(legacyPath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null) return;

            bool mutated = false;

            // Pre-dispatch root-level renames so the dispatcher finds the
            // current key names.
            mutated |= RenameRootKey(root, "recording", "capture");
            mutated |= MigrateCorpusLoggingToTelemetry(root);

            // Pre-dispatch llm-internal renames so the dispatched llm file
            // already carries the canonical key names.
            if (root["llm"] is JsonObject llm)
                mutated |= MigrateLlmLegacyKeys(llm);

            // The legacy JSON key is "capture"; the module directory id is
            // "audio".
            mutated |= DispatchSection(root, "whisp",     "whisp");
            mutated |= DispatchSection(root, "llm",       "llm");
            mutated |= DispatchSection(root, "capture",   "audio");
            mutated |= DispatchSection(root, "telemetry", "telemetry");

            // The legacy paths.modelsDirectory belongs to the whisp module:
            // inject it into the whisp file (just dispatched above, or
            // pre-existing from a partial migration), then strip it from paths.
            if (root["paths"] is JsonObject paths
                && paths["modelsDirectory"] is JsonValue modelsDirNode
                && modelsDirNode.TryGetValue<string>(out string? modelsDir)
                && !string.IsNullOrEmpty(modelsDir))
            {
                InjectIntoModuleFile("whisp", "modelsDirectory", JsonValue.Create(modelsDir));
                paths.Remove("modelsDirectory");
                DeckleSettingsSource.Log.MigrationModelsDirectoryDispatched();
                mutated = true;
            }
            else if (root["paths"] is JsonObject pathsB
                     && pathsB["modelsDirectory"] is not null)
            {
                pathsB.Remove("modelsDirectory");
                mutated = true;
            }

            if (mutated)
            {
                File.WriteAllText(legacyPath, root.ToJsonString(_jsonOptions));
                DeckleSettingsSource.Log.SettingsSplitIntoPerModuleFiles();
            }
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.PerModuleMigrationFailed();
            DeckleSettingsSource.Log.PerModuleMigrationFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    private static bool DispatchSection(JsonObject root, string jsonKey, string moduleId)
    {
        if (root[jsonKey] is not JsonNode section) return false;

        string targetPath = ModuleSettingsFilePath(moduleId);

        if (File.Exists(targetPath))
        {
            // A populated target (a previous launch, or a partial migration
            // that crashed midway) is canonical: only strip the legacy key.
            root.Remove(jsonKey);
            DeckleSettingsSource.Log.MigrationDispatchSkipped(jsonKey, targetPath);
            return true;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, section.ToJsonString(_jsonOptions));
        root.Remove(jsonKey);
        DeckleSettingsSource.Log.MigrationDispatched();
        DeckleSettingsSource.Log.MigrationDispatchedDetail(jsonKey, moduleId);
        return true;
    }

    // Adds the key only when missing, so existing module data wins.
    private static void InjectIntoModuleFile(string moduleId, string key, JsonNode value)
    {
        string targetPath = ModuleSettingsFilePath(moduleId);
        try
        {
            JsonObject moduleRoot;
            if (File.Exists(targetPath))
            {
                string moduleJson = File.ReadAllText(targetPath);
                moduleRoot = JsonNode.Parse(moduleJson) as JsonObject ?? new JsonObject();
            }
            else
            {
                moduleRoot = new JsonObject();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
            }

            if (moduleRoot[key] is null)
            {
                moduleRoot[key] = value.DeepClone();
                File.WriteAllText(targetPath, moduleRoot.ToJsonString(_jsonOptions));
            }
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.InjectFailed();
            DeckleSettingsSource.Log.InjectFailedDetail(key, moduleId, ex.GetType().Name, ex.Message);
        }
    }

    private static string ModuleSettingsFilePath(string moduleId) =>
        System.IO.Path.Combine(AppPaths.UserDataRoot, "modules", moduleId, "settings.json");

    private static void MoveModuleSetting(
        string sourceModuleId,
        string targetModuleId,
        string key)
    {
        string sourcePath = ModuleSettingsFilePath(sourceModuleId);
        if (!File.Exists(sourcePath)) return;

        try
        {
            var source = JsonNode.Parse(File.ReadAllText(sourcePath)) as JsonObject;
            if (source?[key] is not JsonNode value) return;

            string targetPath = ModuleSettingsFilePath(targetModuleId);
            var target = File.Exists(targetPath)
                ? JsonNode.Parse(File.ReadAllText(targetPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            if (target[key] is null)
                target[key] = value.DeepClone();

            source.Remove(key);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, target.ToJsonString(_jsonOptions));
            File.WriteAllText(sourcePath, source.ToJsonString(_jsonOptions));
            DeckleSettingsSource.Log.MigrationDispatched();
            DeckleSettingsSource.Log.MigrationDispatchedDetail(key, targetModuleId);
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.InjectFailed();
            DeckleSettingsSource.Log.InjectFailedDetail(
                key, targetModuleId, ex.GetType().Name, ex.Message);
        }
    }

    private static void RenameModuleSetting(
        string moduleId,
        string oldKey,
        string newKey)
    {
        string path = ModuleSettingsFilePath(moduleId);
        if (!File.Exists(path)) return;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root?[oldKey] is not JsonNode value) return;

            if (root[newKey] is null)
                root[newKey] = value.DeepClone();

            root.Remove(oldKey);
            File.WriteAllText(path, root.ToJsonString(_jsonOptions));
            DeckleSettingsSource.Log.MigrationDispatched();
            DeckleSettingsSource.Log.MigrationDispatchedDetail(newKey, moduleId);
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.InjectFailed();
            DeckleSettingsSource.Log.InjectFailedDetail(
                newKey, moduleId, ex.GetType().Name, ex.Message);
        }
    }

    // An existing target is canonical; the old folder is then left orphaned.
    // Errors are logged, not thrown: settings loss is preferable to a boot
    // failure.
    private static void MigrateModuleFolder(string oldId, string newId)
    {
        string oldDir = System.IO.Path.Combine(AppPaths.UserDataRoot, "modules", oldId);
        string newDir = System.IO.Path.Combine(AppPaths.UserDataRoot, "modules", newId);
        if (!Directory.Exists(oldDir) || Directory.Exists(newDir)) return;
        try
        {
            Directory.Move(oldDir, newDir);
            DeckleSettingsSource.Log.ModuleFolderMigrated();
            DeckleSettingsSource.Log.ModuleFolderMigratedDetail(oldId, newId);
            DeckleSettingsSource.Log.ModuleRenameDetail(oldId, newId);
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.ModuleFolderRenameFailed();
            DeckleSettingsSource.Log.ModuleFolderRenameFailedDetail2(oldId, newId);
            DeckleSettingsSource.Log.ModuleFolderRenameFailedDetail(oldId, newId, ex.GetType().Name, ex.Message);
        }
    }

    private static bool RenameRootKey(JsonObject root, string oldKey, string newKey)
    {
        if (root[oldKey] is not JsonNode legacy || root[newKey] is not null) return false;
        root[newKey] = legacy.DeepClone();
        root.Remove(oldKey);
        DeckleSettingsSource.Log.RenamedRootKey();
        DeckleSettingsSource.Log.RenamedRootKeyDetail(oldKey, newKey);
        return true;
    }

    private static bool MigrateCorpusLoggingToTelemetry(JsonObject root)
    {
        if (root["corpusLogging"] is not JsonObject legacyCorpus
            || root["telemetry"] is not null)
            return false;

        var telemetry = new JsonObject
        {
            ["latencyEnabled"]    = false,
            ["corpusEnabled"]     = legacyCorpus["enabled"]?.GetValue<bool>() ?? false,
            ["recordAudioCorpus"] = legacyCorpus["recordAudioCorpus"]?.GetValue<bool>() ?? false,
            ["storageDirectory"]  = legacyCorpus["dataDirectory"]?.GetValue<string>() ?? "",
        };
        root["telemetry"] = telemetry;
        root.Remove("corpusLogging");
        DeckleSettingsSource.Log.MigratedCorpusToTelemetry();
        return true;
    }

    // The renames chain (manualProfileName → slotAProfileName →
    // primaryRewriteProfileName), so their order matters.
    private static bool MigrateLlmLegacyKeys(JsonObject llm)
    {
        bool mutated = false;

        if (llm["manualProfileName"] is JsonNode legacyManual &&
            llm["slotAProfileName"] is null)
        {
            llm["slotAProfileName"] = legacyManual.DeepClone();
            llm.Remove("manualProfileName");
            DeckleSettingsSource.Log.MigratedLlmManualToSlotA();
            mutated = true;
        }

        if (llm["slotAProfileName"] is JsonNode legacySlotA &&
            llm["primaryRewriteProfileName"] is null)
        {
            llm["primaryRewriteProfileName"] = legacySlotA.DeepClone();
            llm.Remove("slotAProfileName");
            DeckleSettingsSource.Log.MigratedLlmSlotAToPrimary();
            mutated = true;
        }

        if (llm["slotBProfileName"] is JsonNode legacySlotB &&
            llm["secondaryRewriteProfileName"] is null)
        {
            llm["secondaryRewriteProfileName"] = legacySlotB.DeepClone();
            llm.Remove("slotBProfileName");
            DeckleSettingsSource.Log.MigratedLlmSlotBToSecondary();
            mutated = true;
        }

        return mutated;
    }
}
