using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Deckle.Core;

namespace Deckle.Input.Trackpad;

// The Precision Touchpad stack reserves the native three-finger gestures
// (swipe to switch apps / show desktop, three-finger tap). For Deckle's
// macOS-style three-finger drag to exist, those native gestures must be
// silenced first — otherwise Windows and Deckle fight over the same contact.
//
// The act flips the six PrecisionTouchPad DWORDs to 0. Because that mutates a
// user setting, the contract is backup-before-write: the original values are
// snapshotted to a JSON file before the first neutralize, and TryRestore puts
// them back verbatim — a value that was absent is recorded as null and is
// deleted on restore, never recreated.
//
// Whether the Precision Touchpad driver picks up these values live is
// unverified: the change may need a device reconnect or a sign-out to take
// effect, even though the registry write succeeds.
//
// Every registry call is wrapped so the caller (a Settings toggle) never sees
// an exception: failures log through DeckleTrackpadSource and surface as a
// false return.
public static class WindowsGestureNeutralizer
{
    private const string KeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    private static readonly string[] ValueNames =
    {
        "ThreeFingerSlideEnabled",
        "ThreeFingerTapEnabled",
        "ThreeFingerUp",
        "ThreeFingerDown",
        "ThreeFingerLeft",
        "ThreeFingerRight",
    };

    private const string BackupFileName = "gesture-backup.json";

    private sealed record GestureBackup(Dictionary<string, int?> Values);

    private static string BackupPath =>
        Path.Combine(AppPaths.GetModuleDirectory("trackpad"), BackupFileName);

    public static bool TryNeutralize()
    {
        try
        {
            // Backup first, and only if one does not already exist — a second
            // neutralize after a first would otherwise snapshot the zeros and
            // destroy the genuine originals.
            if (!File.Exists(BackupPath))
            {
                var backup = ReadCurrentValues();
                PersistBackup(backup);
                DeckleTrackpadSource.Log.GesturesNeutralizedDetail(SummarizeBackup(backup));
            }

            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (key is null)
            {
                DeckleTrackpadSource.Log.GestureWriteFailed("neutralize", "key_unavailable", "", "");
                return false;
            }

            foreach (string name in ValueNames)
                key.SetValue(name, 0, RegistryValueKind.DWord);

            DeckleTrackpadSource.Log.GesturesNeutralized();
            return true;
        }
        catch (Exception ex)
        {
            DeckleTrackpadSource.Log.GestureWriteFailed(
                "neutralize", "write_threw", ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public static bool TryRestore()
    {
        try
        {
            // No backup file → nothing to restore. Not an error: the act was
            // never run, or has already been restored.
            if (!File.Exists(BackupPath))
                return false;

            string json = File.ReadAllText(BackupPath);
            var backup = JsonSerializer.Deserialize<GestureBackup>(json);
            if (backup?.Values is null)
            {
                DeckleTrackpadSource.Log.GestureWriteFailed("restore", "backup_malformed", "", "");
                return false;
            }

            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (key is null)
            {
                DeckleTrackpadSource.Log.GestureWriteFailed("restore", "key_unavailable", "", "");
                return false;
            }

            foreach (string name in ValueNames)
            {
                if (!backup.Values.TryGetValue(name, out int? saved))
                    continue;

                if (saved is null)
                    key.DeleteValue(name, throwOnMissingValue: false);
                else
                    key.SetValue(name, saved.Value, RegistryValueKind.DWord);
            }

            // Restore done — drop the backup so a subsequent neutralize takes a
            // fresh snapshot of whatever the values are then.
            File.Delete(BackupPath);
            DeckleTrackpadSource.Log.GesturesRestored();
            return true;
        }
        catch (Exception ex)
        {
            DeckleTrackpadSource.Log.GestureWriteFailed(
                "restore", "write_threw", ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public static bool AreNeutralized()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            if (key is null) return false;

            foreach (string name in ValueNames)
            {
                if (key.GetValue(name) is not int v || v != 0)
                    return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            // The safe "not neutralized" reading drives the page's button, so
            // a wrong label is possible: worth a Warning, not silence.
            DeckleTrackpadSource.Log.GestureStateUnreadable(
                "are_neutralized", ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public static bool HasBackup()
    {
        try
        {
            return File.Exists(BackupPath);
        }
        catch (Exception ex)
        {
            // BackupPath creates the module directory, which can throw.
            DeckleTrackpadSource.Log.GestureStateUnreadable(
                "has_backup", ex.GetType().Name, ex.Message);
            return false;
        }
    }

    private static GestureBackup ReadCurrentValues()
    {
        var values = new Dictionary<string, int?>(ValueNames.Length);
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        foreach (string name in ValueNames)
        {
            object? raw = key?.GetValue(name);
            values[name] = raw is int v ? v : null;
        }
        return new GestureBackup(values);
    }

    private static void PersistBackup(GestureBackup backup)
    {
        string json = JsonSerializer.Serialize(
            backup, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(BackupPath, json);
    }

    private static string SummarizeBackup(GestureBackup backup)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (string name in ValueNames)
        {
            if (!first) sb.Append(' ');
            first = false;
            backup.Values.TryGetValue(name, out int? v);
            sb.Append(name).Append('=').Append(v is null ? "null" : v.Value.ToString());
        }
        return sb.ToString();
    }
}
