---
description: Live LogWindow settings and the ambient capture noise gate.
type: agent-instructions
---

# CLAUDE.md — Deckle.Diagnostics.Logging

Owns the live-journal settings. Split from Telemetry by consumer: **human** (interactive viewer, capture-noise filter) here, **machine** (telemetry files, disk gates) there.

Two things worth knowing:

- The SelectorBar level family (`LogWindowVisibilityMode`) is a **viewer-only display lens**: it decides what the live window shows, never what exists. `app.jsonl` is NOT gated by it — the disk journal stays a complete machine record, governed only by the user-authorized Diagnostics gates and the `ApplicationLogToDisk` toggle. (It used to leak into the disk predicate, so switching the window to Alerts silently stopped persisting Verbose; that coupling was removed.) Search text likewise stays UI-local.
- `LogAmbientCaptureActivity` off + an active capture loop drops the ambient providers' `Verbose` and the per-frame `Deckle.Diagnostics.Resource` firehose. This IS an authorized gate (a user toggle), applied provider-level (pre-`BuildEntry`) on both the live window and the disk journal, so it also silences the HUD's own Composition Resource `Verbose` during capture — accepted trade-off.
