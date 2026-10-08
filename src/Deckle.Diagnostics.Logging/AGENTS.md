---
description: Live LogWindow filters and runtime logging gates.
type: agent-instructions
---

# AGENTS.md — Deckle.Diagnostics.Logging

## Settings authority

- Settings > Diagnostics is the sole editing authority, in three groups: **Logging details**, **Application log**, **Telemetry**. Module pages and the LogWindow never duplicate these controls. When details are off, the LogWindow shows a read-only hint that routes to Settings.
- A new provider alone never earns a new setting.

## Verbosity boundaries

- A verbosity boundary follows a chatty activity. It does not follow a whole provider, and it is not just the `Verbose` level. It may reject repetitive `Informational` outcomes of that activity.
- Lifecycle milestones, incidents, recoveries and their one-shot mirrors, every `Warning`/`Error`, and consented telemetry stay outside the gate.
- Content (transcript text, typed text) never enters the operational log. Its producer owns that rule.

## Application log

- `app.jsonl` is an optional operational sink under the diagnostics directory, never the telemetry directory. Telemetry events never enter it or the LogWindow.
- Enabling it keeps a disk-recording privacy confirmation.
- Its `Severity` / `Module` / `Category` recording filter is independent of the LogWindow view filter: changing one never changes the other. It has no text search.

## Viewer projections

- The LogWindow selection is a viewer-only lens: it decides what the window shows, never what exists. Empty means all; values are OR-ed within a dimension and AND-ed across dimensions. It survives window recreation and resets on restart.
- Condensation is viewer-only. It folds consecutive `Verbose` entries with the same provider and event. Copy and Save export raw entries. `Informational`, `Warning` and `Error` repetition is never folded: it is a producer defect.

## Activity controls

Each control below spans the activity named, across every provider that serves it.

- **Ambient**: frame analysis, calculated colours, light pushes, heartbeats, echo attribution. `AmbientEngine` owns the visible lifecycle and the single human `Error` and persistent notification of any fatal stop; Vision and Lighting carry the technical cause in `Verbose` mirrors. A functional fallback (Entertainment to REST, multi-light to group, monitor to primary) is one `Warning` and no notification. Hue EventStream loss does not stop lighting. Discovery, pairing and listing are owned by Settings. Screen-capture recovery is Vision's doctrine.
- **Transcription details**: Dictation and File transcription, across Audio, VAD, Whisp, LLM and delivery providers.
- **Autocorrect activity**: focus and surface probes, decisions, reranking, learning, keyboard rollups, successful corrections.
- **Input activity**: Raw Input frame rollups and per-gesture Trackpad detail. Keyboard detail belongs to Autocorrect activity. Mouse-wheel JSONL capture is telemetry owned by `Deckle.Input`.
- **Windowing**: one technical control, no sub-toggles. Placement, anchoring, z-order, resize, Win32 enumeration and timing are gated at their producers.
