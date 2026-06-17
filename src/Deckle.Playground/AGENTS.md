---
description: Dev-only tuning sandbox — live-adjust the running pipelines without a rebuild, and prototype UX before it migrates to Settings.
type: agent-instructions
---

# AGENTS.md — Deckle.Playground

Dev-only tuning surface. It exists so the running pipelines (HUD composition, Ambient screen-capture + Hue) can be adjusted live, without a rebuild, and as an antechamber to prototype a UX before it migrates into Settings or another final surface. It's built to be extensible — a new pipeline or module gets wired in here to be tuned live, not only the HUD and Ambient pages it carries today. It borrows Settings' routing shape (NavigationView + page Frame, `Type.GetType(tag)`) but the Settings doctrine doesn't apply verbatim — this is a tuning workshop (dense sliders, live previews, programmatically built panels), not a final user page.

## Two persistence regimes

- **HUD tuning is memory-only.** Every HUD knob lives for the process and dies at exit; Reset snaps to the compiled defaults. The point is to *find* the right default, not memorize it — a value that proves correct migrates into the code as a new default. The Playground stores nothing for the HUD. If per-session persistence is ever needed, it's a new dedicated service, never a drift into `AmbientSettingsService` or an ad-hoc file.
- **Ambient tuning shares the real store.** `AmbientPage` reads and writes `AmbientSettingsService` — the same source of truth as the Ambient Settings page, applied live to the running `AmbientEngine`. Deliberately one store, not two: divergent stores for the same knobs would be a trap. Propagation is two-way — Playground and Settings each observe the other's changes through `AmbientSettingsService.Changed`.

`PlaygroundShell` is a `SettingsHost`-style delegate registry (today just `NavigateTo`) so a page never holds a direct reference to the window.
