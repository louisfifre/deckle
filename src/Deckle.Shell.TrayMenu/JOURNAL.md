---
description: Dated diagnostics for Deckle.Shell.TrayMenu — the tray-menu density, gap, and first-click-scroll trail.
---

# Journal — Deckle.Shell.TrayMenu

Dated notes on the tray-menu workstream — the why behind a fix the code no longer shows. Most recent on top.

## 2026-10-12 — The quick-actions panel opened nothing; the carrier now tells its own popup from a dismiss

Reported: Louis never saw the panel. A left click on the icon "does nothing" on v0.39.0, the panel's first release, which no one had run on screen before. His 2026-10-12 session logged no `Deckle-TrayMenu` event: the provider is Verbose behind the Windowing activity, off.

Read in the code: the panel's open path differs from the working menu's in two places. `FlyoutShowMode.Standard` moves keyboard focus into the flyout's popup, a top-level window the carrier owns; the menu is `Transient` and takes no focus. If focusing the popup activates it, Windows deactivates the carrier toward it and `OnCarrierDeactivated` hid the panel inside `Show()`, which reads as nothing happening. The presenter capture also kept any positive size, the `> 0` check the menu bug below fixed. Neither cause is confirmed; nothing ran.

Chose: the carrier subclasses `WM_ACTIVATE` to keep the window activation left for, and raises `Deactivated` only when that window is not owned by the carrier (`TrayWindowOwnership`). A light dismiss of the flyout arms the reopen guard like a deactivation, so a click on the icon still closes the panel when its popup held focus. The capture keeps the presenter size only when it holds the content (`TrayMenuSizing.HoldsItems`) and re-captures after every open. Two warnings are logged whatever the Windowing verbosity: `PanelDismissedWhileOpening` and `PanelSizeRejected`. `ShowMode` stays `Standard`, keyboard use of the panel being the reason for it.

Also: a double click on the icon opens Settings (`DoubleClickRequested`). The shell sends the first click's `WM_LBUTTONUP` before `WM_LBUTTONDBLCLK`, so the panel opens, Settings takes activation and hides it, and the trailing `WM_LBUTTONUP` falls inside the reopen guard.

To verify on screen: a left click opens the panel and it stays; a switch flips without closing it; a click outside, Esc and a second icon click each close it; a double click opens Settings. With the Windowing activity on, an `activation moved to an owned popup` line right after `Quick actions shown` says the focus hypothesis was the cause.

## 2026-10-12 — A captured presenter size is checked against its items

Found: `DefaultMenuFlyoutPresenterStyle` sets `MinHeight` to `MenuFlyoutThemeMinHeight`, 32 DIP (WindowsAppSDK 1.8 generic.xaml), the height of one narrow item. A presenter measured clipped or before its items are laid out reports that height, which is positive: the old `> 0` check let it size the carrier, and the menu opened one item tall and scrolled.

Found in the 2026-07-16 `app.jsonl`: the carrier's `Frame.Loaded`, so the prime cycle, ran about 150 ms after the host was built, before any open. Good opens measured items at 32 DIP, separators at 3 DIP, and the presenter at items + 2 DIP wide and + 6 DIP tall.

Chose: `TrayMenuSizing.Decide` uses the captured presenter only when it is at least as large as the items, otherwise the items plus a 4 DIP margin per side. The sizes are re-captured after every real open. The `Deckle-TrayMenu` events are Verbose and gated by the Windowing activity, so they were absent from the logs when the bug was reported; a rejected capture now raises a Warning that is not gated.

## 2026-10-08 — Module switches leave the MenuFlyout; the custom pill is retired

Decided: the module switches (Ambient Light, Autocorrect, Taskbar cover, Precision scrolling, plus Trackpad and Rewrite) move from the right-click `MenuFlyout` to a left-click quick-actions panel (`TrayQuickActionsHost`), together with Record and "Transcribe audio files…". The right-click menu keeps Logs, Settings, Playground, Restart, Quit.

Why: every `MenuFlyoutItem` click closes the menu, so flipping two switches meant opening the menu twice. A plain `Flyout` holding real `ToggleSwitch` rows stays open while the user toggles, as Windows 11 quick settings do.

Retired: `TraySwitchMenuItem` and `Themes/TrayMenu.xaml` (`ToggleSwitchMenuItemStyle`, the hand-drawn pill and its `PaddingSizeStates` copy) and their merge in `App.xaml`. The pill only existed because a `MenuFlyoutItem` has no slot for a switch; the panel uses the native control. The "Ambient Light: custom pill" and "Custom item template must reproduce PaddingSizeStates" sections of this module's AGENTS.md are now stale.

The carrier window moved to `TrayCarrierWindow`; both hosts own one instance each, with the same pitfalls (layered alpha 0, `WS_POPUPWINDOW` rewrite in `Frame.Loaded`, prime once, measure the real presenter, DPI from the monitor under the anchor, animations off). Not verified at runtime yet: the panel's size on first open, and the close on a second icon click (a 250 ms guard after a `deactivated` hide stops the click from reopening it).

## 2026-05-31 — Bottom mica gap and first-click scroll resolved

Both closed, validated visually and over ~30 openings.

Gap: `FlyoutPlacementMode.Full` stretches the presenter to fill the carrier window (MS docs), so the carrier's measured size dictates the visible size. The old `MeasureFlyout` summed item `DesiredSize` + a flat 8 DIP margin, over-estimating the presenter's real chrome (~4-6 DIP); the surplus painted as an empty mica card at the bottom. It now sizes on the real `MenuFlyoutPresenter.DesiredSize` (captured at the prime cycle) — `Full` has nothing left to stretch. Gap measured at 2.4 DIP, gone.

First-click scroll: on the first render native items use `DefaultPadding` (40 DIP) while the window is sized narrow (32); the `Opened` handler's `GoToState("NarrowPadding")` arrived too late for the first frame. `ApplyNarrowPadding()` sets the narrow padding on all items at build, so the first render is compact from the start.

Trail: a false assertion from the earlier entry — "the layered alpha=0 carrier window doesn't affect the popup" — was wrong (it's the `Full` coupling above) and had steered the earlier fixes toward the code-behind measure alone. Doctrine now in the CLAUDE.md.

## 2026-05-27 — Density imbalance: custom template missing PaddingSizeStates

Observed: native items toggled 40 ↔ 32 DIP across openings while the custom-template Ambient item stayed frozen at ~40.8 — visible imbalance.

First lead (false): items measured detached from the visual tree (`has_visual_parent=false`), `DesiredSize` collapsing toward `MinHeight`. Cached the primed sizes instead of re-measuring — the code-behind measure stabilized, but the visual bug persisted. A confirmed fact (detached items) that wasn't the cause.

Real cause, from reading `generic.xaml`: the native `DefaultMenuFlyoutItemStyle` carries a `VisualStateGroup PaddingSizeStates` (DefaultPadding ≈ 40 / NarrowPadding ≈ 32) the framework switches automatically on mouse/keyboard interaction; the custom `ToggleSwitchMenuItemStyle` template (an earlier un-validated LLM initiative) never reproduced it, so Ambient stayed on its initial padding while the natives went narrow. Fix: reproduce the group verbatim. Doctrine now in the CLAUDE.md.

A native-redesign track stays open, not committed: a native `ToggleMenuFlyoutItem` + a left icon column is the Win11 pattern (Sound, Defender, Network) and would make the scroll class of bug impossible — at the cost of the at-a-glance toggle affordance the custom pill gives.
