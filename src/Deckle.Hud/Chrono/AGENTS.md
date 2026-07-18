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

### Accent and processing reveal — layered over the background

The accent (→ later the living conic) is a *reveal* on top of the background, not a third base colour. Two distinct triggers, same revealed material:

- **During Recording** — a digit flips to **Accent** the instant it advances, and stays Accent until Stop. Trigger = the advance itself (`WriteDigit`).
- **At Stop** — every element drops to Tertiary, then all six digits expose the **living processing material** at full opacity. Dots stay Tertiary. There is no timed reveal and no remembered per-digit filter.

When functional HUD animation is disabled, that material remains fully visible but its rotations park at the canonical phase. The chrono value remains live during Recording regardless of this setting.

### Material revealed at Stop

Each digit becomes a window onto one shared clone of the contour's living conic/comet material. A flat Windows accent twin remains only as the construction-failure fallback. The reveal opacity is static; only the shared material may rotate.
