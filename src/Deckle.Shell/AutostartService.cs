using System;
using Microsoft.Win32;

namespace Deckle.Shell;

// ── AutostartService ─────────────────────────────────────────────────────────
//
// Registers the HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Deckle
// value pointing to the current exe. Windows starts the exe at the next logon.
//
// Why the Run key rather than schtasks.exe:
//
//   - No UAC prompt: HKCU (current user) is accessible as a standard user.
//     Task Scheduler with /RL HIGHEST requires elevation because the task runs
//     elevated.
//   - Deckle has no need for elevation: tray + global hotkey + local
//     transcription. Unnecessary elevation reduces security (BlueHat reports,
//     privilege sprawl).
//   - This is the primitive Windows 11 Settings → "Startup apps" exposes to
//     the user. Toggle aligned with the system mental model.
//
// Multi-install coexistence (dev + publish on the same machine): the Run key
// has a fixed `Deckle` name, so only one install can be in autostart at a time.
// `IsEnabled` compares the stored value with `Environment.ProcessPath`: each
// install only sees ON if it is the one pointed to by the registry. `Disable`
// deletes only if the value belongs to the current exe, to avoid one install
// disabling another. `Enable` always overwrites; enabling from one install
// takes over from the other. Two simultaneous instances would collide on
// RegisterHotKey anyway (err 1409).
//
// All registry calls are wrapped in try/catch: on refusal (machine GPO,
// corrupted profile), log + return false/current state without propagating;
// the Settings toggle must never crash the UI.
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName  = "Deckle";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key?.GetValue(ValueName) is not string s) return false;
            return IsOwnedByCurrentExe(s);
        }
        catch (Exception ex)
        {
            DeckleShellSource.Log.AutostartProbeFailed();
            DeckleShellSource.Log.AutostartProbeFailedDetail(ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public static bool Enable()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            DeckleShellSource.Log.AutostartEnableSkipped();
            return false;
        }

        // Wrap the path in quotes so spaces in the install path don't split
        // it into arguments when Windows launches the entry.
        string command = $"\"{exePath}\"";

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                DeckleShellSource.Log.AutostartEnableFailedAcl();
                DeckleShellSource.Log.AutostartEnableFailedAclDetail(RunKeyPath);
                return false;
            }
            key.SetValue(ValueName, command, RegistryValueKind.String);
            DeckleShellSource.Log.AutostartEnabled();
            DeckleShellSource.Log.AutostartEnabledDetail(command);
            return true;
        }
        catch (Exception ex)
        {
            DeckleShellSource.Log.AutostartEnableFailed();
            DeckleShellSource.Log.AutostartEnableFailedDetail(ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                // No key means no entry — treat as already disabled.
                return true;
            }
            if (key.GetValue(ValueName) is string s && !IsOwnedByCurrentExe(s))
            {
                // Entry belongs to another install of Deckle — leave it alone.
                DeckleShellSource.Log.AutostartDisableSkipped();
                return true;
            }
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            DeckleShellSource.Log.AutostartDisabled();
            return true;
        }
        catch (Exception ex)
        {
            DeckleShellSource.Log.AutostartDisableFailed();
            DeckleShellSource.Log.AutostartDisableFailedDetail(ex.GetType().Name, ex.Message);
            return false;
        }
    }

    // The Run value is stored as `"C:\path\to\exe.exe"` (quoted). An older
    // entry without quotes is also tolerated. Anything after the exe path
    // (arguments) is ignored for ownership comparison.
    private static string? ExtractExePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end < 0 ? null : command.Substring(1, end - 1);
        }
        int space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }

    private static bool IsOwnedByCurrentExe(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        string? stored = ExtractExePath(command);
        if (string.IsNullOrWhiteSpace(stored)) return false;
        string? current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current)) return false;
        return string.Equals(stored, current, StringComparison.OrdinalIgnoreCase);
    }
}
