# AGENTS.md — Deckle.Hud/Chrono

The authoritative colour-state model for the chrono face (`HudChrono`) — the single matrix that decides what colour every element takes in every phase. Vocabulary (elements, animated / at-rest, dots) is fixed in [CONTEXT.md](CONTEXT.md). When the colour logic in `HudChrono.*.cs` and this matrix disagree, this matrix wins.

## Colour-state matrix

### Background tone — the resting colour every element falls to

Layered *under* any accent reveal. Identical for digits and dots. The scale steps down one notch each phase; **Primary is never used on the chrono face**.

| Phase | Background tone |
| --- | --- |
| Charging | **Disabled** |
| Recording (clock running) | **Secondary** |
| Stop — Transcribing / Rewriting | **Tertiary** |

### Accent reveal — layered over the background, two triggers

The accent (→ later the living conic) is a *reveal* on top of the background, not a third base colour. Two distinct triggers, same revealed material:

- **During Recording** — a digit flips to **Accent** the instant it advances, and stays Accent until Stop. Trigger = the advance itself (`WriteDigit`).
- **At Stop** — every element drops to Tertiary, then a **left→right swipe** re-lights, one by one (fade in / out), **each digit that was animated** during the take. Trigger = the swipe wave. At-rest digits and dots stay Tertiary; the wave never touches them.

So at Stop the only differentiator is the wave passing over the formerly-animated digits — the rest of the row is a uniform Tertiary.

### Material revealed by the swipe

Today (step 1) the Stop swipe reveals the **flat accent** (the exact Windows accent colour, `ChronoAccentBrush`). Step 2 will swap that flat accent for the **living conic** the processing stroke samples — same motion, same per-digit filter, the glyph becoming a window onto the shared conic material. The matrix above is unchanged by that swap; only the revealed fill differs.
