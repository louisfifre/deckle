---
description: Structured JSONL persistence and consent gates.
type: agent-instructions
---

# AGENTS.md — Deckle.Diagnostics.Telemetry

Carries the **structured persistence** of telemetry: configures the parent's JSONL listeners and owns the consent gates. Machine-consumer counterpart to Logging.

Notes that aren't obvious from the code:

- The general `app.jsonl` sink has no private view filter. It receives admitted operational observations independently of LogWindow search and display filters; those view filters never affect disk persistence.
- Microphone telemetry defaults closed on a GDPR call: an RMS summary is not voice content but still measures the user's microphone.
- Consent dialogs live in `Deckle.Settings`, not here — they need the Settings window `XamlRoot`. This module owns the gate state and listener behaviour, not the dialog UI.
