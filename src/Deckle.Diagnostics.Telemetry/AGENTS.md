---
description: Purpose-specific telemetry datasets and their consent gates.
type: agent-instructions
---

# AGENTS.md — Deckle.Diagnostics.Telemetry

This module is not the machine-side twin of Logging. The operational log is a separate authority.

- `app.jsonl` is not telemetry: see `Deckle.Diagnostics.Logging/AGENTS.md`. Any application-log wiring left here is migration debt, not doctrine.
- Dataset-only events never enter the LogWindow or `app.jsonl`. If a human needs the same fact, the producer emits a separate operational event.
- Microphone telemetry is off by default, a GDPR call: an RMS summary is not voice content, but it still measures the user's microphone.
- Consent dialogs live in `Deckle.Settings` because they need the Settings window `XamlRoot`. This module owns the gate state and the listener behaviour.
