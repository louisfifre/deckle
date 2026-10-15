using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Xml;

namespace Deckle.Shell;

// Backs the "Start elevated" toggle. Some capabilities (the trackpad driver
// dance, Raw Input from an elevated foreground app) need administrator rights
// from logon. A Task Scheduler task with HighestAvailable run level starts
// Deckle elevated at logon without a UAC prompt at every launch, the PowerToys
// pattern; the single prompt comes when the task is created.
//
// The task and the HKCU\Run value (AutostartService) are two vehicles for the
// same concern, and only one may start the app: enabling the task disables the
// Run key, and Disable restores it, since elevated startup implies startup.
//
// The task name is fixed, so IsEnabled reports ON only when the task's
// <Command> is this exe. Nothing throws to the UI.
public static class ElevatedStartupService
{
    private const string TaskName = "Deckle";

    // Task Scheduler is the source of truth, so the default is "no task": off.
    // Exposed so the "Start elevated" toggle resets to the state a fresh profile
    // starts from, the way AutostartService.DefaultEnabled serves the Run key.
    public static bool DefaultEnabled => false;

    public static bool IsEnabled()
    {
        try
        {
            // Query unelevated: reading a task does not require admin.
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName               = "schtasks.exe",
                    Arguments              = $"/Query /TN {TaskName} /XML",
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                },
            };
            proc.Start();
            string xml = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            // Non-zero → task does not exist (or query failed). Treat as off.
            if (proc.ExitCode != 0) return false;

            return TaskTargetsCurrentExe(xml);
        }
        catch (Exception ex)
        {
            DeckleShellSource.Log.ElevatedStartupProbeFailed();
            DeckleShellSource.Log.ElevatedStartupProbeFailedDetail(ex.GetType().Name, ex.Message);
            return false;
        }
    }

    public static bool Enable()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            DeckleShellSource.Log.ElevatedStartupEnableFailed();
            DeckleShellSource.Log.ElevatedStartupEnableFailedDetail(
                nameof(InvalidOperationException), "Environment.ProcessPath empty");
            return false;
        }

        string tmp = Path.Combine(Path.GetTempPath(), $"Deckle-task-{Guid.NewGuid():N}.xml");
        try
        {
            // Encoding must match the XML declaration (UTF-16) or schtasks
            // mis-decodes the document.
            File.WriteAllText(tmp, BuildTaskXml(exePath), System.Text.Encoding.Unicode);

            // Create the task elevated. runas forces the UAC prompt; the window
            // cannot be hidden with runas, so accept a brief schtasks console.
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName        = "schtasks.exe",
                    Arguments       = $"/Create /TN {TaskName} /XML \"{tmp}\" /F",
                    UseShellExecute = true,
                    Verb            = "runas",
                },
            };
            proc.Start();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                DeckleShellSource.Log.ElevatedStartupEnableFailed();
                DeckleShellSource.Log.ElevatedStartupEnableFailedDetail(
                    "schtasks", $"exit code {proc.ExitCode}");
                return false;
            }

            // The elevated task replaces the Run-key vehicle — only one should
            // start the app at logon.
            AutostartService.Disable();

            DeckleShellSource.Log.ElevatedStartupEnabled();
            return true;
        }
        catch (Exception ex)
        {
            // Win32Exception 1223 == user declined UAC. Same handling as any
            // other failure: log + false.
            DeckleShellSource.Log.ElevatedStartupEnableFailed();
            DeckleShellSource.Log.ElevatedStartupEnableFailedDetail(ex.GetType().Name, ex.Message);
            return false;
        }
        finally
        {
            TryDeleteTemp(tmp);
        }
    }

    public static bool Disable()
    {
        if (!RemoveTask()) return false;

        // Restore the Run-key vehicle so the app still starts at logon: disabling
        // *elevation* is not disabling *startup* (elevated startup implies
        // startup). StartupService.StopStartup() skips this restore on purpose.
        AutostartService.Enable();
        return true;
    }

    // Deletes the elevated task without restoring any other vehicle — the raw
    // removal both Disable() (which then restores the Run key) and
    // StartupService.StopStartup() (which restores nothing) build on.
    internal static bool RemoveTask()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName        = "schtasks.exe",
                    Arguments       = $"/Delete /TN {TaskName} /F",
                    UseShellExecute = true,
                    Verb            = "runas",
                },
            };
            proc.Start();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                DeckleShellSource.Log.ElevatedStartupDisableFailed();
                DeckleShellSource.Log.ElevatedStartupDisableFailedDetail(
                    "schtasks", $"exit code {proc.ExitCode}");
                return false;
            }

            DeckleShellSource.Log.ElevatedStartupDisabled();
            return true;
        }
        catch (Exception ex)
        {
            DeckleShellSource.Log.ElevatedStartupDisableFailed();
            DeckleShellSource.Log.ElevatedStartupDisableFailedDetail(ex.GetType().Name, ex.Message);
            return false;
        }
    }

    private static bool TaskTargetsCurrentExe(string taskXml)
    {
        string? current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current)) return false;

        try
        {
            var doc = new XmlDocument();
            // schtasks may emit a blank line before the declaration, which
            // LoadXml rejects — trim before parsing.
            doc.LoadXml(taskXml.Trim());

            // The Task Scheduler XML lives in a fixed namespace; resolve the
            // Command element without binding to a prefix.
            var commands = doc.GetElementsByTagName("Command");
            if (commands.Count == 0) return false;

            string? command = commands[0]?.InnerText;
            if (string.IsNullOrWhiteSpace(command)) return false;

            string stored = command.Trim().Trim('"');
            return string.Equals(stored, current, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // Battery clauses are off so a laptop on battery still starts Deckle.
    private static string BuildTaskXml(string exePath)
    {
        string userId  = WindowsIdentity.GetCurrent().Name;
        string command = SecurityElementEscape(exePath);
        string user    = SecurityElementEscape(userId);

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts Deckle elevated at logon.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{command}"</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string SecurityElementEscape(string value) =>
        value.Replace("&", "&amp;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;")
             .Replace("\"", "&quot;");

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
