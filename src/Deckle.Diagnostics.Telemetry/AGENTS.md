---
description: Purpose-specific telemetry datasets and their consent gates.
type: agent-instructions
---

# AGENTS.md — Deckle.Diagnostics.Telemetry

Carries purpose-specific telemetry datasets: configures their routed JSONL sinks and owns their explicit consent gates. It is not the machine-consumer counterpart to Logging; the operational application log is a separate authority.

Notes that aren't obvious from the code:

- `app.jsonl` is not telemetry and is outside this module's contract. It belongs to Logging, stores under the diagnostics directory, and mirrors only admitted operational observations. Any current application-log wiring left in this module is migration debt, not doctrine.
- Dataset-only events never enter the LogWindow or `app.jsonl`. If the same fact matters to a human, its producer emits a separate operational event.
- Microphone telemetry defaults closed on a GDPR call: an RMS summary is not voice content but still measures the user's microphone.
- Consent dialogs live in `Deckle.Settings`, not here — they need the Settings window `XamlRoot`. This module owns the gate state and listener behaviour, not the dialog UI.
