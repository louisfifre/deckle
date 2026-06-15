---
name: context-deckle-hud-chrono
description: "Chrono HUD element vocabulary and the per-phase colour-state matrix (digits, dots, animated vs at-rest, the swipe reveal). Read before touching HudChrono colour/state code."
type: agent-instructions
---

# Deckle.Hud — Chrono states

Shared vocabulary and the authoritative colour-state model for the chrono face
(`HudChrono`). The face is the `MM.SS.cc` clock inside the HUD card. This file
fixes the terms Louis and the agents use, and the single matrix that decides
what colour every element takes in every phase. When the colour logic in
`HudChrono.*.cs` and this matrix disagree, this matrix wins.

## Elements — the chrono face

The face reads `MM.SS.cc` as **8 separate TextBlocks** (each a distinct
`UIElement` so it can carry its own Composition visual for the swipe), left to
right:

- **Min1, Min2** — the two minute digits
- **DotA** — the minutes / seconds separator (`.`)
- **Sec1, Sec2** — the two seconds digits
- **DotB** — the seconds / centiseconds separator (`.`)
- **Cs1, Cs2** — the two centisecond digits (*centiseconds*, hundredths)

Each digit has an **accent twin** overlaid in the same cell (`Min1Accent`,
`Sec2Accent`, …), carrying the exact Windows accent colour at `Opacity=0` until
a reveal lifts it. The swipe animator indexes the six digits `0→5`: Min1, Min2,
Sec1, Sec2, Cs1, Cs2.

**Animated digit** — a digit that *advanced* during the take (Recording). The
animated / at-rest distinction is **live only during Recording**. At Stop
nothing moves any more, so there is no animated digit: every element is at rest.
"Was animated at some point" survives Stop **only as the swipe's filter** — it
decides which digits the wave re-lights, never a base colour.

**At-rest digit** — a digit not currently advancing.

**Dot** — DotA, DotB. Never animated; **always takes the at-rest tone**.

## Colour-state matrix

### Background tone — the resting colour every element falls to

Layered *under* any accent reveal. Identical for digits and dots. The scale
steps down one notch each phase; **Primary is never used on the chrono face**.

| Phase | Background tone |
| --- | --- |
| Charging | **Disabled** |
| Recording (clock running) | **Secondary** |
| Stop — Transcribing / Rewriting | **Tertiary** |

### Accent reveal — layered over the background, two triggers

The accent (→ later the living conic) is a *reveal* on top of the background, not
a third base colour. Two distinct triggers, same revealed material:

- **During Recording** — a digit flips to **Accent** the instant it advances,
  and stays Accent until Stop. Trigger = the advance itself (`WriteDigit`).
- **At Stop** — every element drops to Tertiary, then a **left→right swipe**
  re-lights, one by one (fade in / out), **each digit that was animated** during
  the take. Trigger = the swipe wave. At-rest digits and dots stay Tertiary; the
  wave never touches them.

So at Stop the only differentiator is the wave passing over the
formerly-animated digits — the rest of the row is a uniform Tertiary.

### Material revealed by the swipe

Today (step 1) the Stop swipe reveals the **flat accent** (the exact Windows
accent colour, `ChronoAccentBrush`). Step 2 will swap that flat accent for the
**living conic** the processing stroke samples — same motion, same per-digit
filter, the glyph becoming a window onto the shared conic material. The matrix
above is unchanged by that swap; only the revealed fill differs.
