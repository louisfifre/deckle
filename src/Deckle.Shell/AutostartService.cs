using System;
using Microsoft.Win32;

namespace Deckle.Shell;

// Autostart through the HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Deckle
// value, pointing at the current exe. The Run key rather than a scheduled task:
// HKCU needs no UAC prompt, while a /RL HIGHEST task needs elevation to create,
// and the Run key is what Windows 11 Settings > Startup apps shows the user.
//
// The value name is fixed, so only one install (dev or published) can own
// autostart. IsEnabled reports ON only for the install the value points at,
// Disable deletes only its own entry, and Enable always takes over.
//
// Registry refusals (machine GPO, corrupted profile) are logged and reported
// as false, never thrown: the Settings toggle must not crash the UI.
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName  = "Deckle";

    // The registry is the source of truth, so the default is "not registered":
    // off. Exposed so the Startup toggle resets to the state a fresh profile
    // starts from.
    public static bool DefaultEnabled => false;

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
            DeckleShellSource.Log.AutostartProbeFailed(ex.GetType().Name, ex.Message);
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
            DeckleShellSource.Log.AutostartEnableFailed(ex.GetType().Name, ex.Message);
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
                return true;
            }
            if (key.GetValue(ValueName) is string s && !IsOwnedByCurrentExe(s))
            {
                DeckleShellSource.Log.AutostartDisableSkipped();
                return true;
            }
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            DeckleShellSource.Log.AutostartDisabled();
            return true;
        }
        catch (Exception ex)
        {
            DeckleShellSource.Log.AutostartDisableFailed(ex.GetType().Name, ex.Message);
            return false;
        }
    }

    // Enable writes a quoted path; an unquoted value is tolerated too, and any
    // arguments are ignored for the ownership comparison.
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
