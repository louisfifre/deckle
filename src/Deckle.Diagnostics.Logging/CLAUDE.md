---
description: Live LogWindow settings and the ambient capture noise gate.
type: agent-instructions
---

# CLAUDE.md — Deckle.Diagnostics.Logging

Owns the live-journal settings. Split from Telemetry by consumer: **human** (interactive viewer, capture-noise filter) here, **machine** (telemetry files, disk gates) there.

Two non-obvious couplings worth knowing:

- The SelectorBar level family (`LogWindowVisibilityMode`) doesn't only filter the viewer — it's reused by the `app.jsonl` predicate, so the same broad visibility mode also gates disk writes. Search text stays UI-local and never gates disk.
- `LogAmbientCaptureActivity` off + an active capture loop drops the ambient providers' `Verbose` and the per-frame `Deckle.Diagnostics.Resource` firehose. The filter is provider-level (pre-`BuildEntry`), so it also silences the HUD's own Composition Resource `Verbose` during capture — accepted trade-off.
