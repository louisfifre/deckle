# Taskbar cover bug notes

## Band left under a re-created or restacked taskbar

- **Trigger:** `TaskbarCreated` (Explorer restart, or a Windhawk mod re-injecting into the taskbar), `WM_SETTINGCHANGE` or `WM_DISPLAYCHANGE` reached the band while it was visible, and `ABM_GETTASKBARPOS` returned the same rect and edge as before.
- **Observed symptom:** Established in code only: the band's state says visible while the taskbar can be drawn over it, until the next foreground change re-asserts the band's z-order. Louis reported the cover "disappearing" without a visible cause; linking that report to this gap is an interpretation. His diagnostics logs could not confirm it: the z-order is not logged, and `LayoutRebuilt` is Verbose and only recorded on a few days.
- **Cause:** `TaskbarCoverHost.RebuildLayout` returned early when the rect was unchanged, before any `SetWindowPos(HWND_TOPMOST)`. The taskbar (`Shell_TrayWnd`) is topmost too; a re-created taskbar, or one Explorer restacks during the message, lands above the band, and nothing on that path climbed back over it.
- **Violated invariant:** While the cover is visible, the band sits above the taskbar after every layout message, whether or not the taskbar rect changed.
- **Recurrence cue:** An early return or a "nothing changed" shortcut in a layout, display or shell-restart handler that skips the topmost re-assert; or a new path that leaves the band visible after the taskbar may have been repositioned without calling `ReassertTopmost`.
- **Regression coverage:** None. The fix is a `SetWindowPos` decision inside a Win32 message handler on the cover thread; there is no natural seam to reproduce it automatically, and the repository does not accept a seam created only for a test. Watch the recurrence cue in review instead.

## Fullscreen window on another monitor suppresses the band (pending)

- **Trigger:** A window covering its whole monitor is the foreground window, or the shell reports a busy/fullscreen notification state, while the band sits on a different monitor.
- **Observed symptom:** Established in code, not observed in logs: the band hides on its own monitor although nothing there is fullscreen. On a single monitor it cannot happen. Whether Louis's desk has a second monitor is unverified.
- **Cause:** `TaskbarCoverHost.EvaluateAppSuppressed` compares the foreground window's DWM bounds with the foreground window's own monitor, never with the band's monitor, and its `SHQueryUserNotificationState` stage is a session-wide state with no monitor.
- **Violated invariant:** Only a fullscreen window on the band's monitor suppresses the band.
- **Recurrence cue:** A suppression test that reads "is anything fullscreen" without comparing monitors.
- **Regression coverage:** Not fixed yet; planned with the shared foreground service, which exposes the foreground window's monitor and a pure fullscreen predicate that a test can drive.
