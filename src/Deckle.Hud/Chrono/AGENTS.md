---
description: Chrono face of the HUD — tone layers and animation rules.
type: agent-instructions
---

# AGENTS.md — Deckle.Hud/Chrono

The vocabulary for the chrono face (`HudChrono`) is fixed in [CONTEXT.md](CONTEXT.md).

The face treats background tone and feedback reveal as separate visual layers. Primary is deliberately excluded from its tone scale.

Disabling functional HUD animation never suppresses feedback or live data. The processing material stays fully visible at a stable phase, and the chrono keeps updating during Recording.
