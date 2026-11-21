using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Deckle.Core;

namespace Deckle.Input.Trackpad;

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
// That directory is writable by any process of the user, so the extracted file
// is not trusted: elevating "-File <that path>" would let such a process
// choose what runs as administrator. Instead the elevated process starts a
// small launcher passed on the command line (which the user process cannot
// alter once UAC is shown). The launcher reads the file, compares its SHA-256
// with the hash of the embedded resource, and runs the very text it hashed,
// never the path again, so the file cannot be swapped between check and run.
//
// Elevation goes through ShellExecute + the "runas" verb: the user gets one UAC
// prompt. A refusal raises Win32Exception (native code 1223); every failure is
// caught and logged so the Settings button never crashes the UI.
public static class ConnectionRepair
{
    private const string ScriptResourceName =
        "Deckle.Input.Trackpad.Acts.repair-trackpad-connection.ps1";

    private const string ScriptFileName = "repair-trackpad-connection.ps1";

    // Exit codes of the launcher, beside the script's own 0 and 1.
    internal const int ExitScriptModified = 3;
    internal const int ExitScriptUnreadable = 4;

    public static bool TryLaunch()
    {
        try
        {
            (string scriptPath, string sha256) = ExtractScript();

            var psi = new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = BuildArguments(scriptPath, sha256),
                UseShellExecute = true,   // required for the runas verb / UAC
                Verb            = "runas", // elevate; one UAC prompt
            };

            Process? process = Process.Start(psi);
            DeckleTrackpadSource.Log.RepairLaunched();
            if (process is not null) WatchExit(process);
            return true;
        }
        catch (Exception ex)
        {
            int win32Error = ex is Win32Exception w ? w.NativeErrorCode : 0;
            DeckleTrackpadSource.Log.RepairLaunchFailed(
                win32Error == ErrorCancelled ? "uac_declined" : "launch_failed",
                win32Error, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    // ERROR_CANCELLED: the user declined the UAC prompt.
    private const int ErrorCancelled = 1223;

    // Without this the log ends at "launched" and never says whether the
    // repair worked. The script exits 0 when every step ran and 1 when it
    // stopped on a failed step; any other code means it was cut short. Exited
    // runs on a pool thread, after the user closes the console.
    private static void WatchExit(Process process)
    {
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += static (sender, _) =>
            {
                var exited = (Process)sender!;
                try
                {
                    int code = exited.ExitCode;
                    if (code == 0)
                        DeckleTrackpadSource.Log.RepairCompleted(code);
                    else
                        DeckleTrackpadSource.Log.RepairFailed(FailureReason(code), code);
                }
                catch (Exception ex)
                {
                    // The outcome is only reported, never acted on: a read
                    // failure costs the log line, nothing the user sees.
                    DeckleTrackpadSource.Log.RepairExitUnobserved(ex.GetType().Name, ex.Message);
                }
                finally
                {
                    exited.Dispose();
                }
            };
        }
        catch (Exception ex)
        {
            // Watching is best effort: the repair itself already runs.
            DeckleTrackpadSource.Log.RepairExitUnobserved(ex.GetType().Name, ex.Message);
            process.Dispose();
        }
    }

    internal static string FailureReason(int exitCode) => exitCode switch
    {
        1 => "script_failed",
        ExitScriptModified => "script_modified",
        ExitScriptUnreadable => "script_unreadable",
        _ => "script_interrupted",
    };

    // The resource is read once: the same bytes are hashed and written, so the
    // hash is that of what the file should hold.
    private static (string Path, string Sha256) ExtractScript()
    {
        string scriptPath =
            Path.Combine(AppPaths.GetModuleDirectory("trackpad"), ScriptFileName);

        var assembly = Assembly.GetExecutingAssembly();
        using Stream resource = assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException(
                $"embedded resource not found: {ScriptResourceName}");

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();

        File.WriteAllBytes(scriptPath, bytes);

        return (scriptPath, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    internal static string BuildArguments(string scriptPath, string expectedSha256)
    {
        string encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(BuildLauncher(scriptPath, expectedSha256)));
        return $"-NoProfile -EncodedCommand {encoded}";
    }

    // Windows PowerShell 5.1 runs it: no HashData, and ComputeHash instead.
    // Text is decoded from the bytes that were hashed; a BOM is dropped because
    // ScriptBlock.Create would read it as a character before the first comment.
    internal static string BuildLauncher(string scriptPath, string expectedSha256)
    {
        string path = scriptPath.Replace("'", "''");
        return string.Join("\n",
            $"$p = '{path}'",
            $"$expected = '{expectedSha256}'",
            "try { $bytes = [IO.File]::ReadAllBytes($p) } catch {",
            "  Write-Host 'Deckle could not read the repair script, so it was not run.' -ForegroundColor Red",
            "  Read-Host 'Press Enter to close'",
            $"  exit {ExitScriptUnreadable}",
            "}",
            "$actual = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)).Replace('-', '')",
            "if ($actual -ne $expected) {",
            "  Write-Host 'Deckle refused to run the repair script: it is not the one shipped with Deckle.' -ForegroundColor Red",
            "  Read-Host 'Press Enter to close'",
            $"  exit {ExitScriptModified}",
            "}",
            "$text = [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF)",
            "& ([scriptblock]::Create($text))",
            "exit 0");
    }
}
