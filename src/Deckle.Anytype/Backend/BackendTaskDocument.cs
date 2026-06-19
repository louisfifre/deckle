using System.Security;

namespace Deckle.Anytype;

// ── BackendTaskDocument ──────────────────────────────────────────────────────
//
// Composes the Task Scheduler XML for the Anytype backend task. Pure: a function
// of (spec, user, task name) → document string, no I/O — so the shape that the
// whole lifecycle decision rests on is unit-testable without touching schtasks.
//
// The shape is deliberately the inverse of Deckle.Shell.ElevatedStartupService's
// document, and the difference is the point (frozen 2026-06-19, see JOURNAL):
//
//   • NO <Triggers> element. The task never starts on its own — only when Deckle
//     asks it to via `schtasks /Run`. This is how the autostart toggle is honored
//     by construction: no logon trigger means nothing starts the backend unless
//     Deckle decides to.
//   • RunLevel = LeastPrivilege, not HighestAvailable. The backend runs
//     non-elevated, so creating and running the task needs no UAC prompt.
//   • LogonType = InteractiveToken. The process lives in the interactive logon
//     session, the precondition for reading the Credential Manager / DPAPI
//     secrets the wizard wrote in that same session.
//   • ExecutionTimeLimit = PT0S. No time cap: the backend is a long-running
//     server, not a job that should be killed after the default 72h.
//   • AllowStartOnDemand = true. Required for `schtasks /Run` to start it.
//
// Battery clauses are off (a laptop on battery still gets its backend), and
// MultipleInstancesPolicy is IgnoreNew so a redundant /Run while the task process
// is already alive is a no-op rather than a second instance fighting for the port.
internal static class BackendTaskDocument
{
    // Builds the document. userId is the principal the task runs as (the current
    // interactive user); taskName is informational only (it lives in the
    // registration command, not the document body). When the spec carries no
    // arguments the <Arguments> element is omitted entirely — an empty element
    // makes schtasks pass a stray empty argument.
    public static string Build(BackendProcessSpec spec, string userId)
    {
        string user      = Escape(userId);
        string command   = Escape(spec.ExecutablePath);
        string arguments = string.IsNullOrEmpty(spec.Arguments) ? string.Empty : Escape(spec.Arguments);

        string argumentsElement = arguments.Length == 0
            ? string.Empty
            : $"\n          <Arguments>{arguments}</Arguments>";

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Runs the Anytype headless backend on demand for Deckle.</Description>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
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
                  <Command>"{command}"</Command>{argumentsElement}
                </Exec>
              </Actions>
            </Task>
            """;
    }

    // Minimal XML escaping for values injected into the document. Mirrors
    // ElevatedStartupService rather than sharing a helper — the two task chantiers
    // stay self-contained until both land (see JOURNAL 2026-06-19).
    private static string Escape(string value) =>
        value.Replace("&", "&amp;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;")
             .Replace("\"", "&quot;");
}
