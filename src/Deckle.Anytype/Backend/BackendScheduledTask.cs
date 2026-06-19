using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace Deckle.Anytype;

// ── BackendScheduledTask ─────────────────────────────────────────────────────
//
// Owns the Windows Scheduled Task that hosts the Anytype headless backend. Three
// gestures over schtasks.exe, all non-elevated:
//
//   • EnsureRegistered(spec) — write the (triggerless, LeastPrivilege) document
//     and /Create it (/F overwrites). The provisioning step calls this once,
//     after it has downloaded the binary and knows the exe path + serve args.
//   • IsRegistered() — does the task exist? A read, used by the supervisor to
//     tell "backend down, will start it" from "backend not provisioned yet".
//   • Run() — `schtasks /Run`, the on-demand start. Returns to the caller
//     immediately; readiness is observed through the health probe, never a PID.
//
// Why every call is non-elevated (no `runas` verb, hidden window): a
// LeastPrivilege task in the user's own context is created and started without
// administrator rights, so no UAC prompt ever appears. This is the inverse of
// ElevatedStartupService, whose HighestAvailable task needs elevation to create.
// (Empirical residual flagged 2026-06-19: confirm at first real run that neither
// /Create nor /Run of this task raises a UAC prompt.)
//
// The task survives Deckle by construction: the process schtasks starts is
// parented to the Task Scheduler service, not to this caller, so closing or
// rebuilding Deckle does not take the backend down.
//
// Multi-install note: the task name is fixed, so two Deckle installs on the same
// account share one backend task — acceptable because the backend lives at a
// single per-user location (%LOCALAPPDATA%\Programs\Deckle) and is one per user.
// EnsureRegistered overwrites, so the last install wins; no per-exe arbitration
// like ElevatedStartupService needs for the app's own startup task.
public sealed class BackendScheduledTask
{
    // Fixed name, distinct from Deckle.Shell's "Deckle" startup task.
    public const string TaskName = "Deckle Anytype Backend";

    // Registers (or overwrites) the task from the spec. Returns false on any
    // failure, having logged it; never throws to the caller.
    public bool EnsureRegistered(BackendProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        string userId = WindowsIdentity.GetCurrent().Name;
        string xml    = BackendTaskDocument.Build(spec, userId);

        string tmp = Path.Combine(Path.GetTempPath(), $"Deckle-anytype-task-{Guid.NewGuid():N}.xml");
        try
        {
            // Encoding must match the XML declaration (UTF-16) or schtasks
            // mis-decodes the document.
            File.WriteAllText(tmp, xml, Encoding.Unicode);

            int exit = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F");
            if (exit != 0)
            {
                LogFailure("create", $"schtasks exit code {exit}");
                return false;
            }

            DeckleAnytypeSource.Log.BackendTaskRegistered(TaskName);
            return true;
        }
        catch (Exception ex)
        {
            LogFailure("create", $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            TryDeleteTemp(tmp);
        }
    }

    // True when a task by this name exists. A non-zero schtasks exit means the
    // task is absent (or the query failed) — treated as not registered.
    public bool IsRegistered()
    {
        try
        {
            return RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;
        }
        catch (Exception ex)
        {
            LogFailure("query", $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // Starts the task on demand. Returns true when schtasks accepted the start
    // request — not that the backend is up; the supervisor confirms readiness
    // through the health probe.
    public bool Run()
    {
        try
        {
            int exit = RunSchtasks($"/Run /TN \"{TaskName}\"");
            if (exit != 0)
            {
                LogFailure("run", $"schtasks exit code {exit}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LogFailure("run", $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    // Launches schtasks.exe unelevated with output captured (no console flash,
    // no UAC). Returns the process exit code.
    private static int RunSchtasks(string arguments)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = "schtasks.exe",
                Arguments              = arguments,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            },
        };
        proc.Start();
        // Drain both streams so a chatty schtasks cannot deadlock on a full pipe.
        proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static void LogFailure(string operation, string detail)
    {
        DeckleAnytypeSource.Log.BackendTaskOperationFailed();
        DeckleAnytypeSource.Log.BackendTaskOperationFailedDetail(operation, detail);
    }

    private static void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Temp file leak is harmless; never let cleanup throw.
        }
    }
}
