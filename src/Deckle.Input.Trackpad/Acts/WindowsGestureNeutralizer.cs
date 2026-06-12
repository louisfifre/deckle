using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Deckle.Core;

namespace Deckle.Input.Trackpad;

// ── WindowsGestureNeutralizer ────────────────────────────────────────────────
//
// The Precision Touchpad stack reserves the native three-finger gestures
// (swipe to switch apps / show desktop, three-finger tap). For Deckle's
// macOS-style three-finger drag to exist, those native gestures must be
// silenced first — otherwise Windows and Deckle fight over the same contact.
//
// The act flips the six PrecisionTouchPad DWORDs to 0. Because that mutates a
// user setting, the contract is backup-before-write: the original values are
// snapshotted to a JSON file before the first neutralize, and TryRestore puts
// them back verbatim — a value that was absent is recorded as null and is
// deleted on restore, never recreated. The backup is written once and never
// overwritten, so a second neutralize over already-zeroed values cannot lose
// the genuine originals.
//
// Epistemic note: whether the Precision Touchpad driver picks up these registry
// values *live* is unverified. It may require the device to reconnect or the
// session to sign out for the change to take visible effect — the registry
// write succeeds regardless, but the behavioral effect is not guaranteed
// immediate.
//
// Every registry call is wrapped so the caller (a Settings toggle) never sees
// an exception: failures log through DeckleTrackpadSource and surface as a
// false return.
public static class WindowsGestureNeutralizer
{
    private const string KeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

    // The six DWORDs that, set to 0, silence the native three-finger gestures.
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

    // Backup shape: one nullable DWORD per value name. null means the value
    // was absent from the registry at backup time and must be deleted (not
    // recreated) on restore.
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
                DeckleTrackpadSource.Log.GestureWriteFailed(
                    nameof(InvalidOperationException),
                    $"cannot open HKCU\\{KeyPath}");
                return false;
            }

            foreach (string name in ValueNames)
                key.SetValue(name, 0, RegistryValueKind.DWord);

            DeckleTrackpadSource.Log.GesturesNeutralized();
            return true;
        }
        catch (Exception ex)
        {
            DeckleTrackpadSource.Log.GestureWriteFailed(ex.GetType().Name, ex.Message);
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
                DeckleTrackpadSource.Log.GestureWriteFailed(
                    nameof(InvalidDataException),
                    "gesture backup file is empty or malformed");
                return false;
            }

            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (key is null)
            {
                DeckleTrackpadSource.Log.GestureWriteFailed(
                    nameof(InvalidOperationException),
                    $"cannot open HKCU\\{KeyPath}");
                return false;
            }

            foreach (string name in ValueNames)
            {
                if (!backup.Values.TryGetValue(name, out int? saved))
                    continue;

                if (saved is null)
                    // Value was absent originally — delete to match.
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
            DeckleTrackpadSource.Log.GestureWriteFailed(ex.GetType().Name, ex.Message);
            return false;
        }
    }

    // True only when all six values exist and are 0. Never throws.
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
        catch
        {
            return false;
        }
    }

    public static bool HasBackup()
    {
        try
        {
            return File.Exists(BackupPath);
        }
        catch
        {
            return false;
        }
    }

    // Snapshots the current registry values; an absent value is recorded as
    // null so restore knows to delete rather than recreate it.
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

    // Compact k=v summary of what was backed up, for the Verbose mirror.
    // Absent values render as "null" so the log distinguishes them from 0.
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
