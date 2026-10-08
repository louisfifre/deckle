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

## Cover composed before the foreground watcher existed

- **Trigger:** Deckle boots with the taskbar cover's master switch on. `OnLaunched` runs `InitializeStartupModules` (which calls `InitializeTaskbarCover`) before `InitializeStartupShell` (which created and started the `ForegroundWatcher`).
- **Observed symptom:** Established in code, never run: `TaskbarCoverHost` received a null watcher through `_foregroundWatcher!`. Its `Start` then subscribes to `_foreground.Changed` on the UI thread and its band thread reads `_foreground.Current` in `BootstrapState`, both null dereferences; the one on the band thread is unhandled. Whether this crashed a running build is unverified: the ordering came together when the taskbar cover branch moved the cover onto the shared watcher.
- **Cause:** The watcher was created in the shell phase of startup, while its consumers are composed in the earlier modules phase. The null-forgiving `!` hid the ordering from the compiler.
- **Violated invariant:** The shared `ForegroundWatcher` is created and started before any module that subscribes to it is composed.
- **Recurrence cue:** A new consumer of the watcher composed in a startup phase that runs before the watcher's creation, or the watcher moved later in `OnLaunched`; any `_foregroundWatcher!` read at composition.
- **Regression coverage:** None. The order lives in `Deckle.App`'s startup partials, which have no test project; no natural seam reproduces it. The watcher now starts at the head of the taskbar cover step in `App.Startup.Modules.cs`, before the cover and the apps and games runtime.
