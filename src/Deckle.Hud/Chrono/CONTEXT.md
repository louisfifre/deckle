---
name: context-deckle-hud-chrono
description: "Chrono HUD element vocabulary — the face's eight elements, accent twins, and the animated / at-rest / dot distinctions. The normative colour-state matrix lives in this folder's AGENTS.md."
type: agent-instructions
---

# Deckle.Hud — Chrono context

Shared vocabulary for the chrono face (`HudChrono`). The face is the `MM.SS.cc` clock inside the HUD card. This file fixes the terms Louis and the agents use; the authoritative colour-state matrix lives in [AGENTS.md](AGENTS.md).

## Elements — the chrono face

The face reads `MM.SS.cc` as **8 separate TextBlocks** (each a distinct `UIElement` so it can carry its own Composition visual for the swipe), left to right:

- **Min1, Min2** — the two minute digits
- **DotA** — the minutes / seconds separator (`.`)
- **Sec1, Sec2** — the two seconds digits
- **DotB** — the seconds / centiseconds separator (`.`)
- **Cs1, Cs2** — the two centisecond digits (*centiseconds*, hundredths)

**Accent twin** :
The overlay each digit carries in the same cell (`Min1Accent`, `Sec2Accent`, …), holding the exact Windows accent colour at `Opacity=0` until a reveal lifts it. The swipe animator indexes the six digits `0→5`: Min1, Min2, Sec1, Sec2, Cs1, Cs2.

**Animated digit** :
A digit that *advanced* during the take (Recording). The animated / at-rest distinction is **live only during Recording**. At Stop nothing moves any more, so there is no animated digit: every element is at rest. "Was animated at some point" survives Stop **only as the swipe's filter** — it decides which digits the wave re-lights, never a base colour.

**At-rest digit** :
A digit not currently advancing.

**Dot** :
DotA, DotB. Never animated; **always takes the at-rest tone**.
