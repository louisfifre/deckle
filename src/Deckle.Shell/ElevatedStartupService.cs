using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Xml;

namespace Deckle.Shell;

// ── ElevatedStartupService ───────────────────────────────────────────────────
//
// Backs the "Start elevated" toggle. Some Deckle capabilities (the trackpad
// driver dance, Raw Input from an elevated foreground app) need the process to
// run with administrator rights from logon. The clean way to start elevated at
// logon — without nagging the user with a UAC prompt at *every* launch — is a
// Task Scheduler task with HighestAvailable run level: Windows starts it
// elevated, silently, on logon. This is the PowerToys pattern. The single UAC
// prompt happens once, at toggle activation, when the task is created.
//
// Why this lives beside AutostartService and not inside it: it is the *same
// concern* (start Deckle at logon) by a different vehicle (elevated task vs
// HKCU\Run value). The two vehicles are mutually exclusive — only one should
// start the app. So enabling the elevated task disables the Run key, and
// disabling it restores the Run key. The "Start elevated" toggle therefore
// *converts* the autostart vehicle rather than adding a second one (decided in
// the framing session).
//
// NOTE — V1 semantic: "elevated startup implies startup". The General page's
// autostart toggle reads only the HKCU\Run key, so while the elevated task is
// the active vehicle that toggle will read OFF even though Deckle does start at
// logon. Known seam, accepted for V1; a later pass can teach the autostart
// probe to also consult the scheduled task.
//
// Multi-install discipline mirrors AutostartService: the task name is the fixed
// "Deckle", so IsEnabled compares the task's <Command> to this exe's path —
// each install only reports ON for its own task.
//
// Probe (IsEnabled) runs unelevated via `schtasks /Query` — no UAC. Mutation
// (Enable/Disable) needs elevation, so schtasks.exe is launched with the runas
// verb; a brief console flash is unavoidable with runas. UAC refusal raises
// Win32Exception 1223 and is treated as a logged failure. Nothing throws to the
// UI.
public static class ElevatedStartupService
{
    private const string TaskName = "Deckle";

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

            // Restore the Run-key vehicle so the app still starts at logon
            // (V1 semantic: elevated startup implies startup).
            AutostartService.Enable();

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

    // Compares the task's <Command> to this exe's path (case-insensitive,
    // tolerant of surrounding quotes) so each install only sees its own task.
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

    // Task Scheduler XML: logon trigger for the current user, HighestAvailable
    // run level (elevated), action launches the quoted exe. Battery clauses are
    // off so a laptop on battery still starts Deckle.
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

    // Minimal XML escaping for values injected into the task document.
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
