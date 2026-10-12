---
description: Dev-only tuning sandbox for the running pipelines and UX prototypes.
type: agent-instructions
---

# AGENTS.md — Deckle.Playground

- This is a dev-only tuning workshop. The Settings doctrine does not apply verbatim.
- HUD tuning is memory-only and resets to the compiled defaults. Its purpose is to find a default, which then moves into the code. Per-session persistence, if ever needed, is a new dedicated service, never `AmbientSettingsService` or an ad-hoc file.
- The HUD tab changes its previews only. It never writes anything the live HUD reads, `AudioLevelMapper` included: the Recording preview maps its simulated RMS through the live calibration. A value chosen here becomes a default when someone edits the compiled defaults by hand.
- The Ambient tab is the exception to the preview-only rule, by the maintainer's decision (2026-10-12): its scene is the lamps themselves, so it reads and writes the live `AmbientSettingsService`, the same store as the Ambient Settings page, through the module's public `AmbientViewModel`. Whether this exception stays or the rule is reworded is still the maintainer's call.
- Segmentation tuning is an explicit draft: `SegmentationViewModel` edits a snapshot that only Save writes.
- `PlaygroundShell` is a `SettingsHost`-style delegate registry, so a page never holds the window.
