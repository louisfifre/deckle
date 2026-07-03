---
description: Color-science decisions and the Night Owl gamut bug for Deckle.Lighting — read on demand when touching HueColorMath or the ambient color path.
type: module-journal
---

# JOURNAL — Deckle.Lighting

Not read by default. The *why* behind the RGB→Hue color math the code doesn't explain on its own.

## 2026-07-03 — Ambient brightness response is one cubic Bézier

Chose one cubic-Bézier brightness response for Ambient instead of the previous Linear / Gamma / S-Curve / Logarithmic family picker. The runtime and the compact Playground editor must sample the same curve coordinates, so the plotted shape is the contract, not a visualization of separate engine math.

Minimum brightness is now an explicit floor switch plus a retained floor value. Off means dark scenes may go fully black; on means non-dark scenes are raised to the stored floor.

No fine migration was kept for old custom curve-family settings. Old JSON fields can remain ignored; new Bézier fields take their compiled defaults.

## Color science — the Night Owl bug and the gamut/averaging decisions

### The bug

The ambient pipeline rendered deep blues wrong: VS Code Night Owl `#011627` showed up turquoise on a Hue Play / Iris / E14 (Gamut C) instead of blue. `HueColorMath.RgbToHueXyBri` converts sRGB → xy CIE 1931 correctly (gamma decode, Philips Wide Gamut D65 matrix, `X/(X+Y+Z)` projection), but it sent the bridge a **raw chromaticity not clipped to the lamp's Gamut C triangle**. The bridge applies its own proprietary gamut mapping, projecting out-of-triangle points to the nearest edge. For `#011627` the math gives xy `(0.150, 0.203)`, just left of the Gamut C blue corner `(0.1532, 0.0475)`; the bridge projects it onto the B-G edge, where `x≈0.15` maps to a high-G low-B mix → turquoise.

### Fix — client-side Gamut C clip, nearest-edge projection

`HueColorMath.ClipToGamutC(HueXy) → HueXy`: if in-triangle, identity; otherwise project to the nearest point on the triangle via a parametric clamp `t ∈ [0,1]` on each of the three edges, keeping the smallest 2D Euclidean distance in xy. Gamut C corners: `R=(0.6915, 0.3083)`, `G=(0.17, 0.7)`, `B=(0.1532, 0.0475)` (Philips developer docs). Called at the output of `RgbToHueXyBri`. The bridge still does its proprietary clip, but now on an already-in-gamut point — identity on the bridge side. Trade-off: a slight hue shift on points far out of gamut (Night Owl renders as the Hue blue corner — a touch violet, but readably blue, not turquoise). CPU cost negligible vs the HTTP round-trip.

### Rejected alternatives

- **Projection toward white-point D65** `(0.3127, 0.3290)`: desaturates instead of clipping to the nearest corner; for Night Owl it still crosses the B-G edge → same turquoise.
- **Sigmoid gamut-hull compression**: global deformation of the whole scene, under-justified for an ambient lamp, expensive to parametrize.
- **Refactor of the Philips Wide Gamut → sRGB matrix**: the matrix is correct (developer.meethue.com), not the cause. Touch only the gamut mapping.

### Linear-light averaging via 256-entry LUT

Arithmetically summing sRGB bytes amplifies mid-tones vs averaging in linear light. `ColorSpace.SrgbToLinear8Lut` (`float[256]`, ~1 KB) is used at the averaging sites (in `Deckle.Vision`'s `FrameSampler` and `Deckle.Lighting.Ambient`'s zone sampler): sum in float, divide, re-encode via `LinearToSrgb`. LUT rather than per-pixel `MathF.Pow` (pointless cost) and rather than the `x²` gamma-2.0 approximation (visible mid-tone bias, real sRGB gamma is piecewise ~2.4). Simpler and exact. *(These sites live in Vision/Ambient — noted here because they're part of the same color decision.)*

### `ApplyMinBrightness` stays in sRGB

The multiplicative scale `minBri / max` lifting the max channel preserves chromaticity by construction (sRGB R:G:B ratios kept, the Philips matrix is linear). Hue `bri` is derived from `max(R,G,B)`, not `Y` — intentional chromaticity/brightness decoupling, commented atop `HueColorMath.cs`. No refactor justified.

### `ApplySaturationBoost` in OKLCh, not HSV

HSV isn't perceptually uniform: at `V=0.5`, yellow `H=60°` reads ≈0.93 luminance, blue `H=240°` ≈0.07, so a saturation boost brightens yellows and darkens blues — blues wash out when the boost is raised to capture reds. OKLCh (Ottosson 2020) is perceptually uniform: at constant `L`, changing `C` keeps perceived luminance across the wheel. `ApplySaturationBoost` runs `RgbToOklch → C *= boost → OklchToRgb`, early-out on `boost == 1.0`. Same reason the HUD conic stroke uses OKLCh. *(Lives in Ambient.)*

### Windows native doctrine

No native Windows primitive covers xy → Hue Gamut C. WCS and Direct2D Color Management are ICC-profile based, oriented at display calibration, not clipping toward a proprietary Philips triangle. In-house is justified.

### Empirical verification protocol

Perceptual eval by fixed iPhone photo (manual ISO/exposure, reproducible distance/framing), lamp + screen in one frame, on three calibrated scenes before patch and after each measurable step:
- **Scene 1 — Night Owl `#011627` fullscreen static**: deep blue stays blue, not turquoise. (success criterion)
- **Scene 2 — daytime HDR sky** (Forza Horizon beach menu, HDR1000): warm tint preserved, no cyan drift, adaptive exposure keeps biting without crush.
- **Scene 3 — dark HDR game** (Cyberpunk night drive): stays dark with faithful tint, no noise amplification, lamp doesn't light on isolated speculars.

Math validation of `ClipToGamutC` before runtime wiring: 3-4 inline cases (in-gamut D65 identity, just outside blue corner → B-G edge, outside red corner → R-G edge, central white identity).

## Sources

- Philips Hue Developer — Color Conversion Formulas (Wide Gamut matrix + Gamut C corners): https://developers.meethue.com/develop/application-design-guidance/color-conversion-formulas-rgb-to-xy-and-back/
- Björn Ottosson — A Perceptual Color Space for Image Processing (OKLab / OKLCh): https://bottosson.github.io/posts/oklab/
