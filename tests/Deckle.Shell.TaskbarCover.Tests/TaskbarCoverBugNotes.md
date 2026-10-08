# Taskbar cover bug notes

## Band left under a re-created or restacked taskbar

- **Trigger:** `TaskbarCreated` (Explorer restart, or a Windhawk mod re-injecting into the taskbar), `WM_SETTINGCHANGE` or `WM_DISPLAYCHANGE` reached the band while it was visible, and `ABM_GETTASKBARPOS` returned the same rect and edge as before.
- **Observed symptom:** Established in code only: the band's state says visible while the taskbar can be drawn over it, until the next foreground change re-asserts the band's z-order. Louis reported the cover "disappearing" without a visible cause; linking that report to this gap is an interpretation. His diagnostics logs could not confirm it: the z-order is not logged, and `LayoutRebuilt` is Verbose and only recorded on a few days.
- **Cause:** `TaskbarCoverHost.RebuildLayout` returned early when the rect was unchanged, before any `SetWindowPos(HWND_TOPMOST)`. The taskbar (`Shell_TrayWnd`) is topmost too; a re-created taskbar, or one Explorer restacks during the message, lands above the band, and nothing on that path climbed back over it.
- **Violated invariant:** While the cover is visible, the band sits above the taskbar after every layout message, whether or not the taskbar rect changed.
- **Recurrence cue:** An early return or a "nothing changed" shortcut in a layout, display or shell-restart handler that skips the topmost re-assert; or a new path that leaves the band visible after the taskbar may have been repositioned without calling `ReassertTopmost`.
- **Regression coverage:** None. The fix is a `SetWindowPos` decision inside a Win32 message handler on the cover thread; there is no natural seam to reproduce it automatically, and the repository does not accept a seam created only for a test. Watch the recurrence cue in review instead.

## Fullscreen window on another monitor suppresses the band

- **Trigger:** The foreground window reads as fullscreen (its DWM bounds cover its whole monitor, or the shell reports a busy, D3D fullscreen or presentation notification state) while that window sits on a monitor other than the band's.
- **Observed symptom:** Established in code, not observed in logs: the band hides on its own monitor although nothing there is fullscreen. On a single monitor it cannot happen. Whether Louis's desk has a second monitor is unverified.
- **Cause:** Suppression read only "is the foreground fullscreen": first `TaskbarCoverHost.EvaluateAppSuppressed`, then `ForegroundState.IsFullscreen` once the cover moved onto the `ForegroundWatcher`. The geometry stage tests the foreground window against its own monitor and the `SHQueryUserNotificationState` stage is session-wide; neither looked at the band's monitor.
- **Violated invariant:** Only a fullscreen window on the band's monitor suppresses the band.
- **Recurrence cue:** A suppression decision that reads "is anything fullscreen" without comparing the foreground window's monitor with the band's; or a band monitor that is not re-read when the layout changes.
- **Regression coverage:** `CoverSuppressionTests.FullscreenOnAnotherMonitorLeavesTheCoverUp` drives `CoverSuppression.Applies`, the predicate the host uses. The notification-state stage has no monitor of its own; the fix compares the foreground window's monitor for both stages, the best available discriminator.
