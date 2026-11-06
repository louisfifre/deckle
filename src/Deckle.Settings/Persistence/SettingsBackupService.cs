using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Deckle.Core;

namespace Deckle.Settings;

// ── SettingsBackupService ──────────────────────────────────────────────────
//
// Backup & restore for every settings file: the shell's settings.json and
// each module's modules/<id>/settings.json.
//
// A snapshot is a folder "settings-<stamp>" that mirrors the live layout:
//
//   settings-20260101-120000/
//     settings.json                    the shell file
//     modules/<id>/settings.json       one per module that had one
//
// Each file is a byte-identical copy of the live file, so a snapshot stays
// inspectable from a text editor. Modules are found by looking at the
// modules folder, not from a list: a module added later is covered without
// touching this class. Snapshots are never auto-deleted; the user prunes
// them by hand.
//
// Older versions took only the shell file, as a flat "settings-<stamp>.json".
// Those still list, and restoring one replaces the shell file alone, leaving
// the module files as they are (the old snapshot holds none).
//
// The timestamp in a snapshot name has second precision: a second backup in
// the same second finds the folder taken and returns null.
//
// Restoring while the app runs: the shell store is reloaded explicitly. Each
// module store follows its own file (JsonSettingsStore watches it), so the
// restored file is picked up and announced through that store's Changed
// event with no restart.
//
// Fail-soft: every public method returns null, an empty list or false on
// error and logs; nothing here throws into the caller.
public static class SettingsBackupService
{
    private const string FilenamePrefix = "settings-";
    private const string FilenameExtension = ".json";
    private const string FilenameTimestampFormat = "yyyyMMdd-HHmmss";
    private const string PartialSuffix = ".partial";
    private const string ShellFileName = "settings.json";
    private const string ModuleFileName = "settings.json";
    private const string ModulesFolderName = "modules";

    public static string GetDirectory() => SettingsService.Instance.ResolveBackupDirectory();

    public static BackupInfo? CreateBackup()
    {
        try
        {
            // Flush any pending debounced Save first — without this a
            // backup taken right after a slider change would capture the
            // pre-change state still on disk. Only the shell store can be
            // flushed from here (a module is not referenced by this
            // assembly); a module change younger than its 300 ms debounce
            // is not yet on disk.
            SettingsService.Instance.Flush();

            return CreateSnapshot(
                SettingsService.Instance.ConfigPath,
                AppPaths.ModulesDirectory,
                GetDirectory(),
                DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.BackupFailed(ex.GetType().Name, ex.Message);
            return null;
        }
    }

    // The pure half of CreateBackup, over explicit locations so a test can
    // point it at a temporary folder. Builds the snapshot beside its final
    // name and renames it, so a backup interrupted midway never lists as whole.
    internal static BackupInfo? CreateSnapshot(
        string shellFile, string modulesDirectory, string backupDirectory, DateTimeOffset stamp)
    {
        string? staging = null;
        try
        {
            if (!File.Exists(shellFile))
            {
                DeckleSettingsSource.Log.BackupSkippedSourceMissing();
                DeckleSettingsSource.Log.BackupSkippedSourceMissingDetail(shellFile);
                return null;
            }

            Directory.CreateDirectory(backupDirectory);

            string name = FilenamePrefix
                + stamp.ToString(FilenameTimestampFormat, CultureInfo.InvariantCulture);
            string destination = Path.Combine(backupDirectory, name);
            if (Directory.Exists(destination) || File.Exists(destination + FilenameExtension))
                throw new IOException($"A backup named '{name}' already exists.");

            staging = destination + PartialSuffix;
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            File.Copy(shellFile, Path.Combine(staging, ShellFileName));

            foreach (string moduleFile in EnumerateModuleFiles(modulesDirectory))
            {
                string id = Path.GetFileName(Path.GetDirectoryName(moduleFile)!);
                string target = Path.Combine(staging, ModulesFolderName, id);
                Directory.CreateDirectory(target);
                File.Copy(moduleFile, Path.Combine(target, ModuleFileName));
            }

            Directory.Move(staging, destination);
            staging = null;

            DeckleSettingsSource.Log.BackupCreated();
            DeckleSettingsSource.Log.BackupCreatedDetail(destination);
            return new BackupInfo(destination, stamp);
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.BackupFailed(ex.GetType().Name, ex.Message);
            return null;
        }
        finally
        {
            if (staging is not null)
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    DeckleSettingsSource.Log.CleanupFailedDetail("delete_backup_staging", ex.GetType().Name, ex.Message);
                }
            }
        }
    }

    // modules/<id>/settings.json for every module folder that has one.
    private static IEnumerable<string> EnumerateModuleFiles(string modulesDirectory)
    {
        if (!Directory.Exists(modulesDirectory)) yield break;
        foreach (string dir in Directory.EnumerateDirectories(modulesDirectory))
        {
            string file = Path.Combine(dir, ModuleFileName);
            if (File.Exists(file)) yield return file;
        }
    }

    public static IReadOnlyList<BackupInfo> ListBackups() => ListSnapshots(GetDirectory());

    internal static IReadOnlyList<BackupInfo> ListSnapshots(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
                return Array.Empty<BackupInfo>();

            var items = new List<BackupInfo>();

            // Snapshots of this version: a folder whose name carries the stamp.
            // A half-built one ("….partial") fails the parse and stays out.
            foreach (string path in Directory.EnumerateDirectories(dir, FilenamePrefix + "*"))
            {
                if (TryParseTimestamp(path, isFolder: true, out DateTimeOffset stamp)
                    && File.Exists(Path.Combine(path, ShellFileName)))
                    items.Add(new BackupInfo(path, stamp));
            }

            // Snapshots of older versions: the shell file alone.
            foreach (string path in Directory.EnumerateFiles(dir, FilenamePrefix + "*" + FilenameExtension))
            {
                if (TryParseTimestamp(path, isFolder: false, out DateTimeOffset stamp))
                    items.Add(new BackupInfo(path, stamp));
                else
                    // A hand-copied file without the timestamp name still
                    // lists, by its last write time.
                    items.Add(new BackupInfo(path, new DateTimeOffset(File.GetLastWriteTime(path))));
            }

            return items
                .OrderByDescending(b => b.Timestamp)
                .ToList();
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.BackupListFailed(ex.GetType().Name, ex.Message);
            return Array.Empty<BackupInfo>();
        }
    }

    // The caller shows the confirmation dialog beforehand.
    public static bool RestoreFromBackup(string backupPath)
    {
        bool ok = RestoreSnapshot(
            backupPath,
            SettingsService.Instance.ConfigPath,
            AppPaths.ModulesDirectory,
            out bool shellReplaced);

        if (shellReplaced)
        {
            try
            {
                SettingsService.Instance.Reload();
            }
            catch (Exception ex)
            {
                DeckleSettingsSource.Log.RestoreFailed("reload_failed", ex.GetType().Name, ex.Message);
                return false;
            }
        }

        if (ok)
        {
            DeckleSettingsSource.Log.RestoredFromBackup();
            DeckleSettingsSource.Log.RestoredFromBackupDetail(backupPath);
        }
        return ok;
    }

    // The pure half of the restore, over explicit locations. Checks every
    // file of the snapshot before touching a live one, stages them all beside
    // their destinations, then swaps them in, so a bad or unreadable snapshot
    // changes nothing. shellReplaced tells the caller the shell store must
    // reload; module stores follow their files on their own.
    internal static bool RestoreSnapshot(
        string backupPath, string shellFile, string modulesDirectory, out bool shellReplaced)
    {
        shellReplaced = false;
        var staged = new List<(string Temp, string Destination)>();
        try
        {
            bool isFolder = Directory.Exists(backupPath);
            if (string.IsNullOrWhiteSpace(backupPath) || (!isFolder && !File.Exists(backupPath)))
            {
                DeckleSettingsSource.Log.RestoreSkippedSnapshotMissing();
                DeckleSettingsSource.Log.RestoreSkippedSnapshotMissingDetail(backupPath ?? "(null)");
                return false;
            }

            // What the snapshot holds, as (source, live destination). An old
            // snapshot is the shell file itself.
            var plan = new List<(string Source, string Destination)>();
            if (isFolder)
            {
                string shellSource = Path.Combine(backupPath, ShellFileName);
                if (File.Exists(shellSource)) plan.Add((shellSource, shellFile));

                string modulesSource = Path.Combine(backupPath, ModulesFolderName);
                if (Directory.Exists(modulesSource))
                {
                    foreach (string dir in Directory.EnumerateDirectories(modulesSource))
                    {
                        string source = Path.Combine(dir, ModuleFileName);
                        if (!File.Exists(source)) continue;
                        string id = Path.GetFileName(dir);
                        plan.Add((source, Path.Combine(modulesDirectory, id, ModuleFileName)));
                    }
                }
            }
            else
            {
                plan.Add((backupPath, shellFile));
            }

            if (plan.Count == 0)
            {
                DeckleSettingsSource.Log.RestoreFailed(
                    "snapshot_empty", nameof(InvalidDataException), "The backup holds no settings file.");
                return false;
            }

            // A snapshot hand-edited into invalid JSON would otherwise replace
            // the live file, and the store would then silently fall back to
            // defaults: the restore would wipe the settings it meant to bring back.
            foreach (var (source, _) in plan)
            {
                try
                {
                    JsonDocument.Parse(File.ReadAllBytes(source)).Dispose();
                }
                catch (JsonException ex)
                {
                    DeckleSettingsSource.Log.RestoreFailed("snapshot_invalid", ex.GetType().Name, ex.Message);
                    return false;
                }
            }

            // Copy each to a sibling temp file, then move them over the live
            // files, so an interrupted restore never leaves a half-written
            // settings.json on disk.
            foreach (var (source, destination) in plan)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string tmp = destination + ".restore.tmp";
                File.Copy(source, tmp, overwrite: true);
                staged.Add((tmp, destination));
            }

            foreach (var (tmp, destination) in staged.ToList())
            {
                File.Move(tmp, destination, overwrite: true);
                staged.Remove((tmp, destination));
                if (string.Equals(destination, shellFile, StringComparison.OrdinalIgnoreCase))
                    shellReplaced = true;
            }

            return true;
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.RestoreFailed("copy_failed", ex.GetType().Name, ex.Message);
            return false;
        }
        finally
        {
            // A copy that failed after creating temp files would leave them
            // beside the settings files; after a successful Move they no longer exist.
            foreach (var (tmp, _) in staged)
            {
                try { File.Delete(tmp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    DeckleSettingsSource.Log.CleanupFailedDetail("delete_restore_tmp", ex.GetType().Name, ex.Message);
                }
            }
        }
    }

    // A folder name keeps its whole text, so "….partial" does not parse.
    private static bool TryParseTimestamp(string fullPath, bool isFolder, out DateTimeOffset stamp)
    {
        stamp = default;
        string name = isFolder ? Path.GetFileName(fullPath) : Path.GetFileNameWithoutExtension(fullPath);
        if (!name.StartsWith(FilenamePrefix, StringComparison.Ordinal)) return false;

        string token = name.Substring(FilenamePrefix.Length);
        if (DateTime.TryParseExact(token,
                FilenameTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out DateTime parsed))
        {
            stamp = new DateTimeOffset(parsed);
            return true;
        }
        return false;
    }
}

// Path is absolute: a snapshot folder, or the flat file of an older version.
// Timestamp is local time.
public sealed record BackupInfo(string Path, DateTimeOffset Timestamp)
{
    public string DisplayName => Timestamp.LocalDateTime.ToString("g");
}