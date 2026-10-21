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
// Backup & restore for the live shell settings.json.
//
// A snapshot is a flat copy, byte-identical to the live file, so it stays
// inspectable from a text editor. Snapshots are never auto-deleted; the user
// prunes them by hand.
//
// The timestamp in a snapshot name has second precision: a second backup in
// the same second fails File.Copy (overwrite: false) and returns null.
//
// Fail-soft: every public method returns null, an empty list or false on
// error and logs; nothing here throws into the caller.
public static class SettingsBackupService
{
    private const string FilenamePrefix = "settings-";
    private const string FilenameExtension = ".json";
    private const string FilenameTimestampFormat = "yyyyMMdd-HHmmss";

    public static string GetDirectory() => SettingsService.Instance.ResolveBackupDirectory();

    public static BackupInfo? CreateBackup()
    {
        try
        {
            // Flush any pending debounced Save first — without this a
            // backup taken right after a slider change would capture the
            // pre-change state still on disk.
            SettingsService.Instance.Flush();

            string source = SettingsService.Instance.ConfigPath;
            if (!File.Exists(source))
            {
                DeckleSettingsSource.Log.BackupSkippedSourceMissing();
                DeckleSettingsSource.Log.BackupSkippedSourceMissingDetail(source);
                return null;
            }

            string dir = GetDirectory();
            Directory.CreateDirectory(dir);

            DateTimeOffset stampNow = DateTimeOffset.Now;
            string filename = FilenamePrefix
                + stampNow.ToString(FilenameTimestampFormat, CultureInfo.InvariantCulture)
                + FilenameExtension;
            string destination = Path.Combine(dir, filename);

            File.Copy(source, destination, overwrite: false);

            DeckleSettingsSource.Log.BackupCreated();
            DeckleSettingsSource.Log.BackupCreatedDetail(destination);
            return new BackupInfo(destination, stampNow);
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.BackupFailed(ex.GetType().Name, ex.Message);
            return null;
        }
    }

    public static IReadOnlyList<BackupInfo> ListBackups()
    {
        try
        {
            string dir = GetDirectory();
            if (!Directory.Exists(dir))
                return Array.Empty<BackupInfo>();

            var items = new List<BackupInfo>();
            foreach (string path in Directory.EnumerateFiles(dir, FilenamePrefix + "*" + FilenameExtension))
            {
                if (TryParseTimestamp(path, out DateTimeOffset stamp))
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
        string? tmp = null;
        bool replaced = false;
        try
        {
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            {
                DeckleSettingsSource.Log.RestoreSkippedSnapshotMissing();
                DeckleSettingsSource.Log.RestoreSkippedSnapshotMissingDetail(backupPath ?? "(null)");
                return false;
            }

            // A snapshot hand-edited into invalid JSON would otherwise replace
            // the live file, and the store would then silently fall back to
            // defaults: the restore would wipe the settings it meant to bring back.
            try
            {
                JsonDocument.Parse(File.ReadAllBytes(backupPath)).Dispose();
            }
            catch (JsonException ex)
            {
                DeckleSettingsSource.Log.RestoreFailed("snapshot_invalid", ex.GetType().Name, ex.Message);
                return false;
            }

            string destination = SettingsService.Instance.ConfigPath;
            string destinationDir = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(destinationDir);

            // Copy to a sibling temp file then move it over the live file,
            // so an interrupted restore never leaves a half-written
            // settings.json on disk.
            tmp = destination + ".restore.tmp";
            File.Copy(backupPath, tmp, overwrite: true);
            File.Move(tmp, destination, overwrite: true);
            replaced = true;

            SettingsService.Instance.Reload();

            DeckleSettingsSource.Log.RestoredFromBackup();
            DeckleSettingsSource.Log.RestoredFromBackupDetail(backupPath);
            return true;
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.RestoreFailed(
                replaced ? "reload_failed" : "copy_failed", ex.GetType().Name, ex.Message);
            return false;
        }
        finally
        {
            // A copy that failed after creating the temp file would leave it
            // beside settings.json; after a successful Move it no longer exists.
            if (tmp is not null && !replaced)
            {
                try { File.Delete(tmp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    DeckleSettingsSource.Log.CleanupFailedDetail("delete_restore_tmp", ex.GetType().Name, ex.Message);
                }
            }
        }
    }

    private static bool TryParseTimestamp(string fullPath, out DateTimeOffset stamp)
    {
        stamp = default;
        string name = Path.GetFileNameWithoutExtension(fullPath);
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

// Path is absolute; Timestamp is local time.
public sealed record BackupInfo(string Path, DateTimeOffset Timestamp)
{
    public string DisplayName => Timestamp.LocalDateTime.ToString("g");
}
