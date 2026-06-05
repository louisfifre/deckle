---
description: Dated diagnostics for Deckle.Shell.TrayMenu — the tray-menu density, gap, and first-click-scroll trail.
type: module-journal
---

# Journal — Deckle.Shell.TrayMenu

Dated notes on the tray-menu workstream — the why behind a fix the code no longer shows. Most recent on top.

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
