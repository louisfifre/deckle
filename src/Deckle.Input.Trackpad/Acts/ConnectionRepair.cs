using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Deckle.Core;

namespace Deckle.Input.Trackpad;

// ── ConnectionRepair ─────────────────────────────────────────────────────────
//
// The Apple Magic Trackpad 2 driver (appleprecisiontrackpadbluetooth.inf,
// signed, v6.1.8000.6) has a documented bug: pairing does not hold while the
// driver sits in the driver store — the System event log shows "successfully
// paired" then "link key has been removed" ~10 s later. The fix is a multi-step
// pnputil dance (export-backup → delete-driver → re-pair as a generic mouse →
// re-add the driver) that must run elevated and interactively.
//
// Rather than reimplement that procedure in C# — where the user can't follow
// the steps or react to the interactive prompts — the procedure ships as an
// embedded PowerShell script. This act extracts it to the trackpad module
// directory (overwriting each launch so a Deckle update ships an updated
// script) and launches it elevated in a visible console.
//
// Elevation goes through ShellExecute + the "runas" verb: the user gets one UAC
// prompt. A refusal raises Win32Exception (native code 1223); every failure is
// caught and logged so the Settings button never crashes the UI.
public static class ConnectionRepair
{
    private const string ScriptResourceName =
        "Deckle.Input.Trackpad.Acts.repair-trackpad-connection.ps1";

    private const string ScriptFileName = "repair-trackpad-connection.ps1";

    public static bool TryLaunch()
    {
        try
        {
            string scriptPath = ExtractScript();

            var psi = new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,   // required for the runas verb / UAC
                Verb            = "runas", // elevate; one UAC prompt
            };

            Process.Start(psi);
            DeckleTrackpadSource.Log.RepairLaunched();
            return true;
        }
        catch (Exception ex)
        {
            // Win32Exception with NativeErrorCode 1223 == user declined UAC.
            // Treated the same as any other launch failure: log + false.
            DeckleTrackpadSource.Log.RepairLaunchFailed();
            DeckleTrackpadSource.Log.RepairLaunchFailedDetail(ex.GetType().Name, ex.Message);
            return false;
        }
    }

    // Writes the embedded script to the trackpad module dir, overwriting any
    // previous copy so a Deckle update always ships the latest procedure.
    private static string ExtractScript()
    {
        string scriptPath =
            Path.Combine(AppPaths.GetModuleDirectory("trackpad"), ScriptFileName);

        var assembly = Assembly.GetExecutingAssembly();
        using Stream? resource = assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException(
                $"embedded resource not found: {ScriptResourceName}");

        using var file = new FileStream(scriptPath, FileMode.Create, FileAccess.Write, FileShare.None);
        resource.CopyTo(file);

        return scriptPath;
    }
}
