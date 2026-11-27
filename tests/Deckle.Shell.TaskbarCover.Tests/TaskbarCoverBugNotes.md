# Taskbar cover bug notes

## Approach distance read as physical pixels

- **Trigger:** Open the Taskbar cover page on build 0.87.15, where the approach distance is a DIP figure with the unit "px" beside it.
- **Observed symptom:** Louis reported on 2026-11-07 that the distance was "still in pixels, not in DIP". Established by reading the code: the stored value is a DIP figure (`ApproachDistanceDip`), converted per monitor in `CoverGeometry.ApproachSpan` with `GetDpiForMonitor` (effective DPI), once, in `CoverBand.Place`; the migration of the old level keeps 32/64/128 and is covered by `TaskbarCoverSettingsTests`. Nothing is stored or applied in physical pixels. Not run on screen by me; the 0.87.15 behaviour is read from source.
- **Cause:** Wording only. The unit label was "px" (the Windows word for an effective pixel), which a person reads as physical pixels, the misreading the DIP change was made to remove. The description ("Set at 100% scale, it grows with your display scale") did not name DIP.
- **Violated invariant:** The unit shown beside the figure says what the figure is. See the comment on `ApproachDistance.UnitLabel`.
- **Recurrence cue:** A unit string "px" on a setting that holds a DIP figure, or a new distance setting that copies the old card's unit.
- **Regression coverage:** `TaskbarCoverSettingsTests.TheUnitBesideTheFigureIsNeverAPixelWord` guards the constant the card reads, not the rendered text; the description's wording has no test.

## Band left under a re-created or restacked taskbar

- **Trigger:** `TaskbarCreated` (Explorer restart, or a Windhawk mod re-injecting into the taskbar), `WM_SETTINGCHANGE` or `WM_DISPLAYCHANGE` reached the band while it was visible, and `ABM_GETTASKBARPOS` returned the same rect and edge as before.
- **Observed symptom:** Established in code only: the band's state says visible while the taskbar can be drawn over it, until the next foreground change re-asserts the band's z-order. Louis reported the cover "disappearing" without a visible cause; linking that report to this gap is an interpretation. His diagnostics logs could not confirm it: the z-order is not logged, and `LayoutRebuilt` is Verbose and only recorded on a few days.
- **Cause:** `TaskbarCoverHost.RebuildLayout` returned early when the rect was unchanged, before any `SetWindowPos(HWND_TOPMOST)`. The taskbar (`Shell_TrayWnd`) is topmost too; a re-created taskbar, or one Explorer restacks during the message, lands above the band, and nothing on that path climbed back over it.
- **Violated invariant:** While the cover is visible, the band sits above the taskbar after every layout message, whether or not the taskbar rect changed.
- **Recurrence cue:** An early return or a "nothing changed" shortcut in a layout, display or shell-restart handler that skips the topmost re-assert; or a new path that leaves the band visible after the taskbar may have been repositioned without calling `ReassertTopmost`.
- **Regression coverage:** None. The fix is a `SetWindowPos` decision inside a Win32 message handler on the cover thread; there is no natural seam to reproduce it automatically, and the repository does not accept a seam created only for a test. Watch the recurrence cue in review instead.

## Fullscreen window on another monitor suppresses the band

- **Trigger:** The foreground window reads as fullscreen (its DWM bounds cover its whole monitor, or the shell reports a busy, D3D fullscreen or presentation notification state) while that window sits on a monitor other than the band's.
- **Observed symptom:** Established in code, not observed in logs: the band hides on its own monitor although nothing there is fullscreen. On a single monitor it cannot happen. Louis's desk has two monitors (established on 2026-10-15, see the note below).
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

## Secondary taskbars left uncovered

- **Trigger:** Windows shows the taskbar on every display ("Show my taskbar on all displays") and the desktop has more than one monitor.
- **Observed symptom:** Observed by Louis on 2026-10-15: the cover worked on one of his two screens only. Established by a probe the same day: Explorer owned a `Shell_TrayWnd` on the primary monitor and a `Shell_SecondaryTrayWnd` on the second one, both visible, both on the left edge of their monitor; `SHAppBarMessage(ABM_GETTASKBARPOS)` returned the primary's rect alone.
- **Cause:** The host modelled one band and read its geometry from `ABM_GETTASKBARPOS`, the appbar API's description of *the* taskbar. That API only ever knows the primary taskbar; the secondary ones are separate top-level windows it does not report. Nothing in the host could represent a second taskbar, so the second monitor never got a band.
- **Violated invariant:** Every taskbar Windows shows has a band over it, each sized to its own taskbar, on its own monitor, at its own DPI.
- **Recurrence cue:** A layout source that describes one taskbar (the appbar API, `FindWindow("Shell_TrayWnd")` alone, a single rect field); a per-cover state that is not per-band (one alpha, one monitor, one zone); or a rebuild that stops at the first taskbar found. `TaskbarLocator.Locate` is now the one reader of the layout and walks both window classes.
- **Regression coverage:** None reproduces the symptom: the bug lives in a Win32 enumeration with no natural seam, and the repository does not accept a seam created only for a test. The geometry the fix introduced is guarded by `TaskbarPlacementTests`: `EdgeOfASecondaryTaskbarIsReadAgainstItsOwnMonitor` pins that a secondary taskbar's edge comes from its own monitor, and the desktop-order tests pin that a band keeps its taskbar across rebuilds.

## Strip under the taskbar shown over a fullscreen video

- **Trigger:** A maximized browser plays a video fullscreen (Louis, YouTube, 2026-11-26) on a monitor whose taskbar is faded and whose strip under the taskbar shows.
- **Observed symptom:** Louis saw the strip stay over the fullscreen video. Reproduced on 2026-11-27 with a probe window that drops its frame and covers its monitor while staying zoomed: the foreground watcher read it as fullscreen (`Geometry`), the band was suppressed, and the strip still showed.
- **Cause:** `TaskbarCoverHost.RefreshUnderlay` decided from the layout, sleep and Task view only, never from the band's fullscreen suppression. A browser in fullscreen stays `IsZoomed`, so `FindMaximizedApp` kept finding it, and the strip, owned by the app and placed above it, sat over the video. Main had the same gap before the strip learned to fade.
- **Violated invariant:** No strip shows on a monitor whose band a fullscreen app suppresses. See the `possible` gate in `RefreshUnderlay` and the comment above the Task view paragraph of `TaskbarCoverHost.Underlay.cs`.
- **Recurrence cue:** A new reason to show or keep the strip that is not gated by `band.AppSuppressed`; or a fullscreen detection that stops reading a borderless zoomed window as fullscreen.
- **Regression coverage:** None. The decision runs inside the cover thread against real windows, and no existing seam reproduces it. The probe of 2026-11-27 (maximized, then fullscreen, then back: strips 1, 0, 1 after the fix; 1, 1, 1 before) is the check to rerun.

## Strip under the taskbar removed during Alt+Tab

- **Trigger:** Alt+Tab held between maximized apps, on build 0.158.0, with the taskbar faded.
- **Observed symptom:** Louis saw the strip vanish while the switcher was open and come back late. Measured on 2026-11-27: at the press of Tab the foreground became a `XamlExplorerHostIslandWindow` covering the monitor; the strip was removed at once and returned 645 ms after the switcher closed.
- **Cause:** The strip followed the foreground. Alt+Tab's switcher has the class and styles of Task view (measured: same class, `ex=0x00200088`, topmost and tool window), so the rule that takes the strip away in Task view took it away under Alt+Tab too; then a hold and a settle wait delayed its return.
- **Violated invariant:** The strip follows the topmost maximized app on its monitor, not the foreground; a topmost shell window over the app changes nothing. See the header of `TaskbarCoverHost.Underlay.cs` and `TaskbarUnderlay.FindMaximizedApp`.
- **Recurrence cue:** A strip decision that reads `GetForegroundWindow` for anything but Task view, or a wait, hold or threshold added before the strip changes.
- **Regression coverage:** None automatic: the rule walks the live z-order, with no seam a test can drive without real windows. Seen in Louis's trial of 2026-11-27 on the branch `feat/taskbar-strip-alttab`. Task view is still told from the switcher by its English title only (`TaskbarUnderlay.TaskViewShown`).

## Restored window sent behind the maximized app under it

- **Trigger:** A maximized app (T3 Code) behind, a second window (Explorer) maximized over it, then restored to its normal size.
- **Observed symptom:** Louis saw the restored Explorer go behind T3 Code (trial of 2026-11-27). Reproduced the same day with two test windows: the restored window was above the other before its restore and below it after, with only Louis's Deckle running.
- **Cause:** Each strip is owned by its app, and Windows keeps an owner under the windows it owns. On the restore the strip passes to the app behind; `TaskbarUnderlay.Place` put that app's new strip just under the strip of the window above, and Windows raised its owner, the app behind, with it.
- **Violated invariant:** A strip is placed only against a strip of the same app; Deckle never changes the order of the person's windows. See the comment on `TaskbarUnderlay.Place`.
- **Recurrence cue:** A `SetWindowPos` with an insert-after window on a strip whose owner differs from that window's owner; any new owned window placed relative to another app's window.
- **Regression coverage:** None automatic: it takes real owned windows and the window manager. Measured after the fix, the restored window stayed above, but with Louis's older Deckle running in parallel, so the proof is not clean; his own trial confirmed it.
