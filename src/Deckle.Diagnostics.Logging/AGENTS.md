---
description: Live LogWindow filters and runtime logging gates.
type: agent-instructions
---

# AGENTS.md — Deckle.Diagnostics.Logging

Owns the human-facing live journal and the shared filter model/control. Split from Telemetry by consumer: **human** (interactive viewer) here, **machine** (telemetry files and disk gates) there. Runtime emission gates also live here because they silence logging before either consumer.

Two things worth knowing:

- The `Severity` / `Module` / `Category` selection is a **viewer-only display lens**: it decides what the live window shows, never what exists. Empty dimensions mean all; values within a dimension are OR-ed and active dimensions are AND-ed. The selection survives lazy-window recreation for the current process, then resets on restart. Search stays UI-local.
- `app.jsonl` never reads the live selection. When its filter editor is exposed, it reuses the same model/control with independent persisted state, so disk policy cannot change when someone filters the viewer during an investigation.
- `LogAmbientCaptureActivity` off + an active capture loop drops the ambient providers' `Verbose` and the per-frame `Deckle.Diagnostics.Resource` firehose. This IS an authorized gate (a user toggle), applied provider-level (pre-`BuildEntry`) on both the live window and the disk journal, so it also silences the HUD's own Composition Resource `Verbose` during capture — accepted trade-off.
