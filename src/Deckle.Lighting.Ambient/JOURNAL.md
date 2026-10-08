---
description: Dated findings for Deckle.Lighting.Ambient — how a light colour is derived from the sampled frame, and the near-black decisions.
type: module-journal
---

# Journal — Deckle.Lighting.Ambient

Dated notes on the colour path from the sampled frame to the lights — the why behind a choice the code no longer shows. Most recent on top.

## 2026-10-08 — How a zone colour is derived, and where it is quantised

Found, reading `FrameSampler.Readback.cs`, `AmbientZoneSampler`, `AmbientColorPipeline` and the two push files before changing them.

- The grid cells are sRGB bytes. SDR: the captured BGRA8 pixel of each cell, re-encoded to a byte after the exposure multiplier when exposure is not 0. HDR: each FP16 scRGB cell is tone-mapped (Hable, content peak, exposure) and encoded to a byte by `ColorSpace.ScRgbToSrgb`.
- Group mode: `FrameSampler` sums the cells in linear light (the BGRA8 path with the exposure-scaled floats; the HDR path re-linearises the tone-mapped bytes through `SrgbToLinear8Lut`), divides by the cell count, and encodes the mean back to an sRGB byte: `SampledFrame.Average`.
- Multi mode: `AmbientZoneSampler.SampleZone` reads the byte cells of the zone's band, sums them through `SrgbToLinear8Lut`, divides by the count and encodes the mean back to an sRGB byte. The per-light brightness scale then multiplies those encoded bytes and rounds.
- After that, every stage takes and returns bytes: saturation (OKLCh from bytes, rounded back to bytes), the Bézier curve on the max channel (rounded per channel), the floor (rounded), the EMA (float state, rounded output), the floor again (rounded). Group mode rounds to a byte six times before the push (average, saturation, curve, floor, EMA, floor); multi mode seven (zone mean and brightness scale instead of the average).
- Before tuning, `PreparePushColor` replaced any colour whose three channels are at or below `OffThreshold = 8` with (0, 0, 0). With the floor off, Hue REST turns (0, 0, 0) into `on:false`.
- With a shadow-lifting curve this makes a hard step. Under the Ambient preset curve (0.18, 0.55, 0.40, 0.90), an encoded 8/255 is forced to 0 while 9/255 comes out near 26/255 (8/255 itself would map near 24/255). The EMA spreads that step over a few ticks; it does not remove it. Under the default curve (0.42, 0, 1, 1), 8/255 maps near 0.46/255 and 9/255 near 0.58/255, so the byte rounding after the curve already decides black or not at that level.

Chose, after this reading:

- Exact-zero black gate. `OffThreshold` is gone. A light is sent black only when its source is exactly black: `SampledFrame.AverageLinear` (group) or the zone's linear mean (multi) equal to (0, 0, 0), which happens only when every covered cell is the byte 0. A dark non-black frame is tuned like any other.
- Float path. The sampler publishes the unrounded linear average; the zone sampler returns the linear mean. Tuning, smoothing and the floor work on `SrgbColor`, three sRGB-encoded floats in [0, 1], because the curve, the floor and the EMA were defined on encoded bytes and keep that meaning (a floor of N raises the max channel to N/255). Saturation runs in OKLCh on linear light through the float overloads added to `ColorSpace`. The per-light brightness scale multiplies the encoded channels, as it did the bytes.
- One quantisation per light and per tick, after smoothing and the floor re-application, right before the Playground publication and the push gate.
- The EMA is `AmbientColorSmoother`, per key, and applies the stored alpha (one 1/15 s step) over the measured time since the previous push tick.
- A black latch in the quantisation (`AmbientBlackLatch`, per key): a lit key goes black below 0.25/255 on the max channel, a black key lights again above 0.75/255, and a lit value that would round to 0 is lifted to one step. Why: of the oscillation tests (60 Hz sequences pushed at 50 Hz, default and Ambient preset curves, alphas 0.30 and 0.08, floor off), none showed black / non-black alternation with plain rounding — black alternating with 1/255 or 2/255 stayed black under the default curve and lit under the Ambient curve. One failed: a steady 8/255 under the default curve became black at every tick, because the curve maps it to ~0.46/255 and rounding sends that to 0. The latch is the smallest change that keeps it lit; a key starts lit unless it is already below 0.25/255.
- Known limit of the latch, from its definition: under the default curve, a source rising from black to a level that maps between 0.25/255 and 0.75/255 (about 6 to 10/255 encoded) stays black until it rises further.
