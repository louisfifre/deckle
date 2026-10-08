---
description: Dev-only tuning sandbox for the running pipelines and UX prototypes.
type: agent-instructions
---

# AGENTS.md — Deckle.Playground

- This is a dev-only tuning workshop. The Settings doctrine does not apply verbatim.
- HUD tuning is memory-only and resets to the compiled defaults. Its purpose is to find a default, which then moves into the code. Per-session persistence, if ever needed, is a new dedicated service, never `AmbientSettingsService` or an ad-hoc file.
- Ambient tuning is not here: it moved to the Ambient Settings page, which reads and writes `AmbientSettingsService`.
- Segmentation tuning is an explicit draft: `SegmentationViewModel` edits a snapshot that only Save writes.
- `PlaygroundShell` is a `SettingsHost`-style delegate registry, so a page never holds the window.
