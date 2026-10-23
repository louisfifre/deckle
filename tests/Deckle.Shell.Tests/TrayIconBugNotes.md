# Tray icon bug notes

## The tray icon fell back to the generic icon without a line

- Trigger: Deckle's own `.ico` is not next to the exe (assets not copied) or Windows refuses to load it.
- Symptom: the tray shows the generic application icon and the log says nothing, so the wrong mark looks like a design choice.
- Cause: `TrayIconManager.LoadIconFromFile` returned the shared `IDI_APPLICATION` icon with no event on either failure path.
- Violated invariant: a fallback is a Warning that names what was asked and what was obtained.
- Fix: `DeckleShellSource.TrayIconFallback(state, reason, win32_error)`, a Warning. `reason` is `asset_missing` or `load_failed`; `win32_error` is 0 when the file is missing.
- Recurrence cue: a tray icon that is not Deckle's own and no `The tray icon fell back` line.
- Guard: `TrayIconFallbackObservabilityTests`, which runs the real loader in a test output that has no icon folder. The `load_failed` branch is not tested: it needs a file Windows refuses, and there is no seam for one.