---
description: Every HUD tunable the Playground exposed until its removal on 2026-11-13, grouped as the Playground grouped them, with the value the HUD ships and what each group changes on screen. The starting point for a redesign of the HUD rendering.
---

# HUD tuning reference

The Playground was the developer window where the HUD outline, its materials, its rotations and the pause gauge were tuned by hand. It was removed on 2026-11-13. The values Louis had settled on there became the compiled defaults, and Deckle no longer reads a tuning file: the HUD now looks the same on every machine.

This note keeps those values in one place for the day the HUD rendering is redesigned. It lists the 61 tunables the Playground offered, in its own sections and order, with the value the code holds today.

## Where the values live

- The outline, its materials, its rotations and the digit reveal: `HudComposition.ConicArcStrokeConfig`, in `src/Deckle.Composition/Core/HudComposition.Config.cs`. Its header comment is the lexicon of every field.
- The pause gauge's own knobs: `UtteranceCountdownConfig`, in `src/Deckle.Composition/Core/UtteranceCountdownConfig.cs`. The gauge also reads the Recording section's fades and material.
- `HudChrono` holds one instance of each, built from these defaults (`StrokeConfig` in `src/Deckle.Hud/Chrono/HudChrono.Stroke.cs`, `CountdownConfig` in `HudChrono.UtteranceCountdown.cs`). Changing a default and rebuilding is the only way to change the HUD.
- A `modules\hud\settings.json` left on a machine by an older Deckle is never read. The last one Louis saved held fourteen keys: eleven are now the defaults below, `retreatSeconds` (0.33) already was, and three (`cutSeconds`, `cutEaseP1X`, `cutEaseP2X`) named fields the code had already dropped, so they changed nothing.

## How to read the tables

- A turn is one full revolution: 0.25 turn is 90°.
- Direction: +1 is clockwise, −1 counter-clockwise.
- An ease is a CSS `cubic-bezier(x1, y1, x2, y2)`: two handles on the unit square. `(0, 0, 1, 1)` is a straight line, constant speed.
- Lightness and chroma are OKLCh values. OKLCh is a colour space where equal steps look equal to the eye: lightness runs from 0 (black) to 1 (white), chroma is the saturation, 0 being grey.
- The Playground sometimes showed a value in another unit than the code stores (percent, pixels). The tables give the stored value, and the Playground's form in brackets.
- "Shipped" marks the eleven values that changed on 2026-11-13, when the tuned values became the defaults.

## HUD geometry

The outline's thickness and how far it sits outside the HUD card. Shared by Recording, Transcribing, Rewriting and the gauge.

| Playground label | Field | Value |
|---|---|---|
| Stroke thickness (dip) | `StrokeThickness` | 5 dip (shipped, was 4) |
| InsetDip | `InsetDip` | −3 dip (shipped, was −2). Negative places part of the stroke outside the card. |

## Colour material

The colour wheel the outline shows when a state's material is Colour: how bright, how saturated, which hues and how finely they are cut.

| Playground label | Field | Value |
|---|---|---|
| OklchLightness | `OklchLightness` | 0.65 (shipped, was 0.75) |
| OklchChroma | `OklchChroma` | 0.3 |
| HueStart | `HueStart` | 0 (red at 3 o'clock) |
| HueRange | `HueRange` | 1 (the full rainbow) |
| WedgeCount | `WedgeCount` | 360 (a smooth ring) |

## Grey material

The flat grey the outline shows when a state's material is Grey, one per theme.

| Playground label | Field | Value |
|---|---|---|
| GreyLightnessDark | `GreyLightnessDark` | 1 (white on the dark theme) |
| GreyLightnessLight | `GreyLightnessLight` | 0 (black on the light theme) |

## Sweep mask

The shape of the lit part of the outline while it spins (Transcribing, Rewriting): how long each comet is, how its two ends fade, and whether a second comet runs opposite.

| Playground label | Field | Value |
|---|---|---|
| ConicSpanTurns | `ConicSpanTurns` | 0.5 turn |
| ConicLeadFadeTurns | `ConicLeadFadeTurns` | 1 turn (with the tail, more than the span: the fades meet in a pure bell) |
| ConicTailFadeTurns | `ConicTailFadeTurns` | 1 turn |
| ConicFadeCurve | `ConicFadeCurve` | 4 (a crisp comet; 1 would be a linear ribbon) |
| ArcMirror | `ArcMirror` | on (two comets, opposite each other) |

## Hue rotation

How the colour wheel turns under the comets, so the colour at a comet's head walks the rainbow.

| Playground label | Field | Value |
|---|---|---|
| HuePeriodSeconds | `HuePeriodSeconds` | 14 s per turn |
| HueDirection | `HueDirection` | +1 (clockwise) |
| HueEaseP1.X, P1.Y, P2.X, P2.Y | `HueEaseP1X`, `HueEaseP1Y`, `HueEaseP2X`, `HueEaseP2Y` | (0.125, 0.375, 0.875, 0.625): fast at the seam, slower mid-turn, never still |
| HueMinSpeedFraction | `HueMinSpeedFraction` | 0 (no floor needed with this ease) |

## Sweep rotation

How the comets themselves travel around the outline: what the eye reads as the speed of the loading animation.

| Playground label | Field | Value |
|---|---|---|
| ArcPeriodSeconds | `ArcPeriodSeconds` | 8 s per turn |
| ArcDirection | `ArcDirection` | +1 (clockwise) |
| ArcEaseP1.X, P1.Y, P2.X, P2.Y | `ArcEaseP1X`, `ArcEaseP1Y`, `ArcEaseP2X`, `ArcEaseP2Y` | (0.125, 0.375, 0.875, 0.625), the same shape as the hue |
| ArcMinSpeedFraction | `ArcMinSpeedFraction` | 0 |

## Recording

The outline while Deckle listens: still, its brightness following the microphone, and the shape of its two lobes. The pause gauge takes its fades and its material from here.

| Playground label | Field | Value |
|---|---|---|
| Material | `RecordingMaterial` | Grey |
| RecordingHueShiftTurns | `RecordingHueShiftTurns` | 0 (shows only on Colour) |
| RecordingBlendSeconds | `RecordingBlendSeconds` | 2 s |
| RecordingConicSpanTurns | `RecordingConicSpanTurns` | 0.5 turn |
| RecordingConicLeadFadeTurns | `RecordingConicLeadFadeTurns` | 1 turn |
| RecordingConicTailFadeTurns | `RecordingConicTailFadeTurns` | 1 turn |
| RecordingConicFadeCurve | `RecordingConicFadeCurve` | 3.5 (shipped, was 2): the lobes fade harder toward the sides |
| RecordingArcMirror | `RecordingArcMirror` | on |
| RecordingArcPhaseTurns | `RecordingArcPhaseTurns` | 0 turn |

## Transcribing

The outline while the speech is turned into text.

| Playground label | Field | Value |
|---|---|---|
| Material | `TranscribingMaterial` | Grey |
| TranscribingHueShiftTurns | `TranscribingHueShiftTurns` | 0 (shows only on Colour) |
| TranscribingOpacity | `TranscribingOpacity` | 1 |
| TranscribingBlendSeconds | `TranscribingBlendSeconds` | 2 s |

## Rewriting

The outline while the language model rewrites the text: the one state on the colour wheel.

| Playground label | Field | Value |
|---|---|---|
| Material | `RewritingMaterial` | Colour |
| RewritingHueShiftTurns | `RewritingHueShiftTurns` | 0 |
| RewritingOpacity | `RewritingOpacity` | 1 |
| RewritingBlendSeconds | `RewritingBlendSeconds` | 2 s |

## Digit reveal

The cone of colour (or grey) the clock's six digits show while Deckle transcribes or rewrites: where it radiates from, its own palette and its own rotation, apart from the outline's.

| Playground label | Field | Value |
|---|---|---|
| Centre X | `CloneCentreXFraction` | 196/272 ≈ 0.7206 of the row's width (196 px on the 272-wide row) |
| Centre Y | `CloneCentreYFraction` | 0 (the row's top edge) |
| Lightness | `CloneOklchLightness` | 0.9 (shows only on Colour) |
| Chroma | `CloneOklchChroma` | 0.3 (shows only on Colour) |
| Hue period (s) | `CloneHuePeriodSeconds` | 7 s |
| Hue direction | `CloneHueDirection` | −1 (counter-clockwise) |
| Sweep period (s) | `CloneArcPeriodSeconds` | 4 s |
| Sweep direction | `CloneArcDirection` | −1 (counter-clockwise) |

## Initial orientations

Where the colour wheel and the comets stand when the outline is built. Recording keeps the wheel still at this pose.

| Playground label | Field | Value |
|---|---|---|
| HuePhaseTurns | `HuePhaseTurns` | 0 turn |
| ArcPhaseTurns | `ArcPhaseTurns` | 0 turn |

## Gauge

The light that opens along the top and bottom edges during a pause in dictation, and closes when speech resumes or the utterance is cut. Reach is how far it runs along an edge from its centre: 0 is the centre, 1 the corners.

| Playground label | Field | Value |
|---|---|---|
| Start reach (%) | `StartReach` | −0.25 [−25 %] (shipped, was 0): the gauge stays closed through the first part of a pause |
| End reach (%) | `EndReach` | 1 [100 %] (shipped, was 1.75): full at the corners, exactly at the cut |
| Course (curve) | `CourseEaseP1X`, `CourseEaseP1Y`, `CourseEaseP2X`, `CourseEaseP2Y` | (0.66, 0, 1, 0.33) (shipped, was (0.55, 0, 0.85, 1)): almost still through the first half of the pause, then faster and faster to the cut |
| Follow (s) | `FollowSeconds` | 0.1 s (shipped, was 0.167) |
| Retreat (s) | `RetreatSeconds` | 0.33 s |
| Retreat ease (curve) | `RetreatEaseP1X`, `RetreatEaseP1Y`, `RetreatEaseP2X`, `RetreatEaseP2Y` | (0, 0, 0, 1): fast out, slow in, back to rest |

With the start below zero and this course, the light appears only once the course has run a fifth of its way, about 69 % into the pause, so it shows during the last 30 % or so. This is computed from the curve, not measured on screen.
