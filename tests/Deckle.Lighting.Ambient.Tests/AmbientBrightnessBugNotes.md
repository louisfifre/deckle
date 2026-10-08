# Ambient brightness bug notes

## Minimum brightness yielded to the dark-sample cutoff

- **Trigger:** Ambient Lighting sampled a frame at or below the dark-sample threshold while Minimum brightness was enabled with a positive floor.
- **Observed symptom:** The emitted RGB became black and Hue turned the lamp off instead of keeping it at the selected minimum brightness.
- **Cause:** `AmbientColorPipeline.ApplyTuning` returned black before consulting `MinBrightnessEnabled`, and its floor operation separately treated pure black as exempt.
- **Violated invariant:** While Minimum brightness is enabled, screen-derived output stays at or above the selected floor, including for a black frame; disabling the floor allows black again.
- **Recurrence cue:** A dark-content or off-threshold branch returns black before the minimum-brightness policy, or post-processing such as smoothing runs after the last floor application.
- **Regression coverage:** `AmbientBrightnessCurveTests.MinimumBrightnessEnabledKeepsBlackSampleAtFloor` reproduces a black sampled frame and asserts the emitted neutral RGB remains at the selected floor.

## Near-black frames flickered the lamp on and off

- **Trigger:** Ambient Lighting with Minimum brightness off on dark content whose sampled average hovers around 8/255 encoded, under a shadow-lifting brightness curve (the Ambient preset curve 0.18 / 0.55 / 0.40 / 0.90).
- **Observed symptom:** The lamp flickered between off and a visible dim light on dark scenes (reported in use; not re-measured at the lamps for this note).
- **Cause:** `PreparePushColor` replaced any average whose three channels were at or below `OffThreshold = 8` with (0, 0, 0) before tuning, while the curve lifted 9/255 to about 26/255. A one-byte move in the sampled average became a step between off and about 26/255; the EMA spread that step over a few ticks but did not remove it. Each stage also rounded to bytes, six to seven times before the push.
- **Violated invariant:** A light goes black only when its source is exactly black (linear mean exactly (0, 0, 0)); the colour stays in float until a single quantisation at the output.
- **Recurrence cue:** A threshold, epsilon or byte comparison that decides black before the output quantisation; a stage that rounds to bytes before the push; a curve that maps a non-black value under half an 8-bit step, which plain rounding turns into black (the reason for the black latch in `AmbientBlackLatch`: enter black below 0.25/255, leave above 0.75/255).
- **Regression coverage:** `AmbientNearBlackTests.DarkButNonBlackFrameStaysLitWithFloorOff` (8/255 under the identity curve stays lit, exact black gives black). The oscillation tests in `AmbientNearBlackTests` (steady black, steady 8/255, black alternating with 1/255 and 2/255, at 50 Hz over a 60 Hz sequence) guard the 8-bit output; only steady 8/255 under the default curve needed the latch, and none of those sequences alternated with plain rounding. `AmbientNearBlackTests.LevelsAcrossTheRoundingBoundaryDoNotToggleBlackOnceLatched` is the evidence for the latch: a grey sequence switching between 6/255 and 10/255 every 0.5 s (default curve maps them to ~0.26/255 and ~0.71/255, either side of half a step) makes plain rounding toggle black at least twice after the first second, for alpha 0.30 and 0.08, while the latch toggles at most once.

## Change threshold had no effect on Hue Entertainment

- **Trigger:** Ambient Lighting driving Hue Entertainment (a continuous output, `RequiresContinuousColorUpdates` true, 50 Hz), with any Change threshold value.
- **Observed symptom:** Moving the Change threshold changed nothing at the lamps; every small tick-to-tick change was streamed. Established by reading the code, not measured at the lamps.
- **Cause:** `AmbientPushGate.ShouldDrop` returned false whenever the output was continuous, so the threshold was only read on Hue REST. The bypass existed because a continuous stream must send a colour on every tick, and dropping was the gate's only answer.
- **Violated invariant:** Change threshold acts on every transport. Under the threshold a persistent output gets no push and a continuous output gets the previous pushed colour again (in multi mode, every mapped light stays in the frame); a change to exact black and the first push after start always go out.
- **Recurrence cue:** A transport flag (`RequiresContinuousColorUpdates`, a preferred rate) that short-circuits a tuning gate instead of changing what the gate sends.
- **Regression coverage:** `AmbientPushGateTests.ContinuousOutputsHoldThePreviousColourUnderTheThreshold` asserts a sub-threshold change on a continuous output is a hold of the previous colour, not a push of the new one.