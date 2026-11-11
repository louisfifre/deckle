# Crash relaunch policy bug notes

## A deliberate kill read as a crash

- **Trigger:** the supervised Deckle process ends through .NET's `Process.Kill`, `Stop-Process -Force` or a script built on them. The exit code is -1 (0xFFFFFFFF).
- **Observed symptom:** `IsCrash(-1)` was true, so the supervisor relaunched a process a person had just stopped and spent relaunch budget. The window is narrow (the supervisor sleeps, then checks whether the session is ending, and the same scripts usually kill the launcher first). Confirmed by reading the code; the -1 of Process.Kill is the runtime's behavior as known, not read from this repository.
- **Cause:** the test was the high bit alone, and 0xFFFFFFFF has it.
- **Violated invariant:** a deliberate end never counts as a crash.
- **Recurrence cue:** a `CrashRelaunched` row with code `0xFFFFFFFF` right after a manual stop.

Regression: `CrashRelaunchPolicyRegressionTests.An_exit_through_Process_Kill_is_not_a_crash`.

## A startup that stopped halfway left Deckle running with no icon and no hotkeys

- **Trigger:** an exception thrown while the startup builds the tray icon and its menus, before the hotkeys are registered. Three times on 2026-11-11, each a few seconds after an Explorer crash, while the shell was coming back: 01:12:44 (`Shell_NotifyIcon(NIM_ADD)` refused in `TrayIconManager.Register`), 01:52:30 and 12:59:18 (`AppWindow.IsShownInSwitchers` refused with E_NOTIMPL in the `TrayCarrierWindow` constructor).
- **Observed symptom:** Deckle ran for the rest of the session with no tray icon and no hotkeys, while its modules worked (the taskbar cover, the injected taskbar engine). The log holds one `SurvivedException` with `origin=ui_thread` and nothing after the `hudwindow` milestone: no `tray`, no `hotkeys`. Launching Deckle again did nothing visible, because the surviving process held the resident mutex and each new one exited (`second-instance.txt`). A restart of Explorer did not help either: no tray icon manager existed to hear `TaskbarCreated`. Only Task Manager could end it.
- **Cause:** Established from the stack traces and the code. `App.OnLaunched` ran its startup phases with nothing around them, and the UI thread's `UnhandledException` net marks every exception handled so the app lives. That net is right for a fault in a running app and wrong here: it turned a startup that could not finish into a process with no surface.
- **Violated invariant:** A resident Deckle has its tray icon and its hotkeys, or it is not running. `App.EndFailedStartup` holds the rule: a failure before the end of `InitializeStartupShell` ends the process with `CrashRelaunchPolicy.StartupFailedExitCode`, which the launcher reads as a crash and answers with its one relaunch.
- **Recurrence cue:** a `SurvivedException` with `origin=ui_thread` whose stack passes through `InitializeStartup*`; a session whose milestones stop before `hotkeys`; a `Deckle.exe` in Task Manager with no icon in the notification area. Also any new startup phase added to `OnLaunched` outside the guarded block while the icon does not exist yet.

Regression: `CrashRelaunchPolicyRegressionTests.A_startup_that_could_not_finish_is_relaunched_like_a_crash` pins that the launcher relaunches on that exit code. The guard in `OnLaunched` itself has no test: it needs a WinUI application whose startup throws, and the self-check runs unsupervised. The two refusals that triggered it are fixed where they happened (`TrayIconBugNotes.md`, `TrayMenuBugNotes.md`).
