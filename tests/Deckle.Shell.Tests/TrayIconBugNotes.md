# Tray icon bug notes

## The tray icon fell back to the generic icon without a line

- Trigger: Deckle's own `.ico` is not next to the exe (assets not copied) or Windows refuses to load it.
- Symptom: the tray shows the generic application icon and the log says nothing, so the wrong mark looks like a design choice.
- Cause: `TrayIconManager.LoadIconFromFile` returned the shared `IDI_APPLICATION` icon with no event on either failure path.
- Violated invariant: a fallback is a Warning that names what was asked and what was obtained.
- Fix: `DeckleShellSource.TrayIconFallback(state, reason, win32_error)`, a Warning. `reason` is `asset_missing` or `load_failed`; `win32_error` is 0 when the file is missing.
- Recurrence cue: a tray icon that is not Deckle's own and no `The tray icon fell back` line.
- Guard: `TrayIconFallbackObservabilityTests`, which runs the real loader in a test output that has no icon folder. The `load_failed` branch is not tested: it needs a file Windows refuses, and there is no seam for one.
## A refused first add cost the icon and the hotkeys for the whole run

- Trigger: Deckle starts while the shell cannot take a tray icon yet. On 2026-11-11 (01:12:44) Explorer had crashed three seconds earlier and was coming back.
- Symptom: `Shell_NotifyIcon(NIM_ADD) failed` thrown out of `TrayIconManager.Register`, then no icon and no hotkeys for the whole run (the startup stopped there, see `CrashRelaunchPolicyBugNotes.md`).
- Cause: the first add was the one call to the shell that was never retried. A re-add after `TaskbarCreated` already ran a spaced sequence of attempts; the first add threw at the first refusal and disposed the owner window, so nothing was left to hear Explorer come back.
- Violated invariant: an add the shell refuses is retried, the first one like any other, and the owner window outlives a refusal.
- Fix: `TrayIconRegistration.Add` opens the sequence a re-add runs and returns its outcome; `TrayIconManager.Register` starts the retry timer instead of throwing. `MaxReaddAttempts` went from 5 to 10, half a second apart, to cover the seconds Explorer takes to come back.
- Recurrence cue: a `The shell refused the tray icon at startup` line with no `The tray icon is on the taskbar after a retry` after it; any new call in `Register` that throws on a shell refusal.
- Guard: `TrayIconRegistrationTests.AFirstAddTheShellRefusesIsRetriedUntilItIsTaken` and `ARefusedFirstAddCountsAsTheFirstAttemptOfTheSequence`, against the fake shell. The timer that spaces the attempts lives in `TrayIconManager`, which needs a real window, and is not driven by a test. Not yet seen live on a refusing shell.
