# Ambient brightness bug notes

## Minimum brightness yielded to the dark-sample cutoff

- **Trigger:** Ambient Lighting sampled a frame at or below the dark-sample threshold while Minimum brightness was enabled with a positive floor.
- **Observed symptom:** The emitted RGB became black and Hue turned the lamp off instead of keeping it at the selected minimum brightness.
- **Cause:** `AmbientColorPipeline.ApplyTuning` returned black before consulting `MinBrightnessEnabled`, and its floor operation separately treated pure black as exempt.
- **Violated invariant:** While Minimum brightness is enabled, screen-derived output stays at or above the selected floor, including for a black frame; disabling the floor allows black again.
- **Recurrence cue:** A dark-content or off-threshold branch returns black before the minimum-brightness policy, or post-processing such as smoothing runs after the last floor application.
- **Regression coverage:** `AmbientBrightnessCurveTests.MinimumBrightnessEnabledKeepsBlackSampleAtFloor` reproduces a black sampled frame and asserts the emitted neutral RGB remains at the selected floor.
