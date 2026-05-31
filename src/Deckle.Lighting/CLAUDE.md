---
name: claude-deckle-lighting
description: "Doctrine for Deckle.Lighting, the Philips Hue driver (REST CLIP v1/v2) and color science pipeline. Read before touching the Hue driver, the gamut mapping logic, or the EventStream external-change detection."
type: agent-instructions
module: Deckle.Lighting
---

# CLAUDE.md — Deckle.Lighting

Driver module for external light outputs. Covers the driver-agnostic `ILightOutput` abstraction, the direct REST Hue implementation (`HueRestLightOutput`) with discovery, pairing, control and entertainment configurations, and the color math stack converting RGB → Hue xy (`HueColorMath`) with client-side Gamut C mapping. The module is consumed by `Deckle.Lighting.Ambient` (live driving from screen capture) and by `Deckle.Playground` (isolated bridge testing, calibration).

The pipeline deliberately takes the REST CLIP v1 path capped at ~10-20 Hz, not the Entertainment v2 DTLS-PSK path. 100% C#, zero native dependencies, zero third-party NuGet — the cadence is sufficient for an ambient mode with smoothing on top. The Entertainment v2 path stays archived for later if perception justifies it; the swap will happen behind the `ILightOutput` abstraction without touching the rest of the pipeline.

## Module structure

The `Hue/` folder carries the entire Hue stack: `HueDiscovery.cs` (cloud lookup `discovery.meethue.com`), `HueBridgeClient.cs` (HTTPS cert bypass + CLIP v1 pairing + control), `HueRestLightOutput.cs` (`ILightOutput` implementation on top of the REST client), `HueColorMath.cs` (RGB sRGB → xy CIE 1931 conversion math + Hue bri + Gamut C clip), plus the DTOs `HueBridge`, `HueGroup`, `HueLight`, `HueEntertainmentArea`. At the module root, `ILightOutput.cs`, `LightDescriptor.cs`, `LightColor.cs` make up the agnostic abstraction. Bootstrap code (bridge connection parameters, IP validation, persistence of the CLIP API key `username`) lives in `Deckle.Lighting.Ambient/AmbientSettings.cs` since that is the consumer doing the orchestration. The driver itself is stateless outside of its `HttpClient` and its `username`.

## Color science pipeline

Canonical doc for the color science pass run on the ambient lighting pipeline. Covers the cause of the Night Owl `#011627 → turquoise` bug, the math decisions and their rationale, and the anti-patterns ruled out.

### Context

The ambient pipeline historically produced incorrect chromatic rendering on deep blues. VS Code Night Owl `#011627` renders turquoise on a Hue Play / Iris / E14 lamp (Gamut C) instead of blue. `HueColorMath.RgbToHueXyBri` converts sRGB RGB to xy CIE 1931 correctly (gamma decode, Philips Wide Gamut D65 matrix, `X/(X+Y+Z)` projection) then sends the result to the bridge. The bridge receives a raw chromaticity, not clipped to its Gamut C triangle, and applies its own proprietary gamut mapping that projects out-of-triangle points to the nearest edge. For `#011627` the math gives linear `(0.0003, 0.0071, 0.0179)` → `X=0.00420, Y=0.00569, Z=0.01816` → xy `(0.150, 0.203)`. This point sits just to the left of the Gamut C blue corner `(0.1532, 0.0475)` and the bridge projects it onto the B-G edge, where `x≈0.15` maps to a high-G low-B mix — turquoise rendering.

Two secondary latent biases surface on reading, not responsible for the static Night Owl bug but affecting complex scenes. Arithmetic averaging in gamma-encoded sRGB inside `FrameSampler.ReadGridBGRA8` (`Deckle.Vision`) and `AmbientEngine.SampleZone` (`Deckle.Lighting.Ambient`) — two cascaded stages that amplify mid-tones. `ApplySaturationBoost` (`AmbientEngine.cs`) operates in HSV, which suffers from exactly the yellow/blue luminance asymmetry already documented as the reason for migrating the HUD conic stroke to OKLCh.

### Gamut mapping client-side, nearest-edge projection

`HueColorMath.ClipToGamutC(HueXy) → HueXy` method. If the xy point is in-triangle Gamut C, identity. Otherwise, project to the nearest point on the triangle via parametric clamp `t ∈ [0, 1]` on each of the three edges (Red↔Green, Green↔Blue, Blue↔Red), keeping the projection with the smallest 2D Euclidean distance in the xy plane. The Gamut C corners are `R=(0.6915, 0.3083)`, `G=(0.17, 0.7)`, `B=(0.1532, 0.0475)` (Philips Hue developer docs reference). Called at the output of `RgbToHueXyBri`, before returning to the caller. `HueBridgeClient` keeps sending raw xy to the bridge, which keeps doing its proprietary clip but now on a point that is already in-gamut, so identity on the bridge side.

**Alternatives rejected.** Projection toward white-point D65 `(0.3127, 0.3290)` shifts out-of-gamut points toward cyan or violet depending on the edge crossed — for Night Owl `#011627`, it crosses the B-G edge so produces the same turquoise rendering, doesn't fix the bug. Sigmoid gamut hull compression imposes a global deformation on the whole scene, under-justified for an ambient lamp and expensive to derive the parameters. Nearest-edge minimizes chromaticity ΔE by construction and lets deep blues saturate on the Hue blue corner instead of fleeing along the B-G edge.

**Trade-off.** Slight hue shift on points significantly out-of-gamut. Night Owl `#011627` will render as "Hue blue corner" — a bit more violet than a perfect cobalt, but readably blue, not turquoise. CPU cost: three dot products and three clamps per push, negligible compared to the HTTP round-trip to the bridge that dominates latency.

### Linear-light averaging via LUT 256-entry

sRGB encodes luminance via a gamma curve ≈ 2.4. Arithmetically summing sRGB bytes amplifies mid-tones compared to averaging in linear-light, which respects photometry. `ColorSpace.SrgbToLinear8Lut` (`float[256]` static readonly initialized via `SrgbToLinear(i / 255f)`, ~1 KB memory). The three averaging sites (`FrameSampler.ReadGridBGRA8`, `FrameSampler.ReadGridFP16`, `AmbientEngine.SampleZone`) sum in `float`/`double`, divide by count, re-encode via `LinearToSrgb`. LUT rather than per-pixel `MathF.Pow` (~30 k pow/s, measurable but pointless) and rather than the `x²` approximation (gamma 2.0) which visibly biases the mid-tones since the real sRGB gamma is piecewise with a 2.4 exponent outside the linear toe. LUT is simpler and exact.

### `ApplyMinBrightness` stays in sRGB

The multiplicative scale `scale = minBri / max` applied to sRGB bytes to lift the max-channel up to `minBri` preserves chromaticity by construction (the R:G:B ratios in sRGB space are preserved, the Philips matrix is linear). The only theoretical bias is on luminance perception, already handled by the fact that Hue `bri` is derived from `max(R,G,B)` and not from `Y` (intentional chromaticity/brightness decoupling, commented at the top of `HueColorMath.cs`). Refactor not justified.

### `ApplySaturationBoost` in OKLCh

HSV is not perceptually uniform: at `V=0.5`, a yellow `H=60°` has perceived luminance ≈ 0.93, a blue `H=240°` ≈ 0.07. A saturation boost modifies luminance perception differently depending on hue — a ×1.5 boost on a yellow makes it brighter, on a blue makes it darker. On the ambient lamp, this bias translates into blues that look washed out when the boost is raised to capture reds. OKLCh is perceptually uniform by design (Björn Ottosson 2020): at constant `L`, modifying `C` (chroma) preserves perceived luminance across the whole wheel. Pipeline sRGB byte → linear via LUT → cone responses cube root → OKLab → OKLCh, via `ColorSpace.RgbToOklch` symmetric to `OklchToRgb`. `ApplySaturationBoost` therefore operates as `RgbToOklch → C *= boost → OklchToRgb` with an early-out on `boost == 1.0`. Cross-module consistency: the project already made the OKLCh choice for the HUD conic stroke for exactly this reason.

### Rejected anti-patterns

- **Projection toward white-point D65 for gamut mapping.** Desaturates the out-of-gamut instead of clipping it to the nearest corner. Does not fix the Night Owl bug since the traversal goes through the B-G edge, same turquoise rendering.
- **Gamma 2.0 (`x²`) to save the `Pow`.** Visible bias on mid-tones since the real sRGB gamma is piecewise with a 2.4 exponent outside the linear toe. LUT is simpler and exact.
- **Saturation boost in HSV with ad-hoc luminance correction.** Temptation to compensate the HSV asymmetry with a corrective factor on V. Reinvents OKLCh poorly, loses wheel symmetry.
- **Refactor of the Philips Wide Gamut → sRGB matrix.** The current matrix is correct (referenced developer.meethue.com), not the cause of the bug. Touch only the gamut mapping.

### Windows native doctrine

No native Windows primitive covers xy → Hue Gamut C. WCS (Windows Color System) and Direct2D Color Management are ICC-profile based, oriented toward display calibration — not clipping toward a proprietary Philips triangle. In-house code justified.

### Empirical verification

Perceptual evaluation is done by fixed iPhone photo (manual ISO/exposure, reproducible distance and framing) framing lamp + screen in the same frame, on three calibrated scenes before patch and after each measurable step. **Scene 1 — Night Owl `#011627` fullscreen static**: success criterion, deep blue stays blue on the lamp, not turquoise. **Scene 2 — daytime HDR sky** (Forza Horizon beach menu capture on HDR1000 display): warm tint preserved, no cyan drift, adaptive exposure keeps biting without crush. **Scene 3 — dark HDR game scene** (Cyberpunk 2077 night drive): stays dark with faithful tint, no noise amplification, lamp does not light up on isolated specular highlights. Math validation of `ClipToGamutC` before runtime wiring: 3-4 cases in an inline test method (in-gamut central D65 identity, just outside blue corner projection on B-G edge, outside red corner projection on R-G edge, central white identity).

## Hue discovery, pairing, control

Three distinct phases in a bridge lifecycle from the driver side.

**Discovery** via cloud lookup `discovery.meethue.com` (HTTPS without cert pinning). Returns `0..N` bridges with their `bridge_id` (hex16 serial number) and their `bridge_ip` (local LAN IPv4). Manual IP fallback if cloud discovery fails.

**Pairing** via CLIP v1 — the user presses the link button on the bridge, the driver does `POST /api` with a device-type identifying the app, and receives a `username` (REST application key) and a `clientkey` (DTLS PSK for Entertainment v2, never displayed in clear). 30 s timeout; during the wait, `error 101` is normal (link not pressed yet) and stays `Verbose`.

**Control** via HTTPS cert bypass — the bridge uses a self-signed certificate with the serial number as common name, `HttpClientHandler.ServerCertificateCustomValidationCallback` bypass configured hardcoded. CLIP v1 endpoints used: `PUT /groups/{id}/action` (single-colour group push), `PUT /lights/{id}/state` (per-light push), `GET /groups` (listing), `GET /lights` (listing). CLIP v2 endpoints used: `GET /resource/entertainment_configuration` (retrieves the per-light XYZ positions stored, feeds the `LightZoneSuggester` on the Ambient side), `GET /resource/light` and `GET /resource/grouped_light` (retrieve the v2_uuid → v1_id map needed to resolve EventStream events — see the dedicated section below). `tt_ds` (Hue `transitiontime` in deciseconds, 1 = 100 ms) is forced to 1 by the ambient driver to override the factory default of 4 (= 400 ms) which would lag the lamp.

## EventStream v2 — external command detection

The bridge exposes an SSE flow `GET /eventstream/clip/v2` (header `hue-application-key`, payload `text/event-stream`) which pushes state changes of every resource in near real-time. The driver consumes this flow via `HueBridgeClient.StreamEventsAsync(onUpdate, ct)` — long-running task started by `AmbientEngine` at engine startup, 2 s reconnect on any closure (clean or network error), terminates on cancel. Native .NET 10 `System.Net.ServerSentEvents.SseParser<T>` does the parsing — zero third-party NuGet.

The goal is to detect when an external command (Philips Hue app, Home Assistant, physical Dimmer Switch button, voice assistant, scene activation) modifies a managed lamp, and to **stop the engine cleanly** rather than trying to take back control. Reclaim attempts (immediate re-push to overwrite the external change) were tried in V0 and ruled out: too fragile (some scene transitions did not produce an individual reclaimable event, the bridge overwrites our push), and above all the desired experience is not a push war — it is that the user knows when their ambient stopped for an external reason. The logic lives on the `AmbientEngine.OnResourceUpdate` side: log `ExternalChangeStopped`, then `Task.Run(Stop)` to marshal off the SSE thread. User-facing handling of this notification (toast, dialog, banner) belongs to a future error-handling pass; for now the toggle falls back to off and the LogWindow carries the reason.

**Self vs external discrimination.** The bridge re-emits our own `PUT` calls to the SSE flow — without discrimination we would have a false positive that stops the engine on every one of our own pushes. The chosen pattern is *state echo classification*: `AmbientEngine` stores the Hue state it has just pushed, namespaced by `group:<v1_id>` or `light:<v1_id>`, with the local `DateTimeOffset.UtcNow` push timestamp. On event receipt, `AmbientHueEchoClassifier` compares the partial EventStream payload (`on`, `bri`, `xy`) with that last pushed state. If the payload matches within tolerance and the local age is within the 2 s `EchoWindow`, it is our own echo, ignored. If the payload differs, or if the matching echo arrives after that window, it is treated as an external change and the engine stops. The Hue `creationtime` stays informational only: comparing local receipt time to local push time avoids bridge/host clock skew, while the state comparison covers late bridge echoes that exceeded the old pure timing window.

**v2 ↔ v1 map.** EventStream events carry v2 ids (UUIDs), whereas REST push uses v1 ids (integers). `HueBridgeClient.FetchV2IdMapsAsync` does a single fetch at engine startup and returns two dicts (`Lights` v2_uuid → v1_id, `GroupedLights` v2_uuid → v1_group_id) that the engine caches for the whole session. If a lamp is added to the bridge mid-session, we miss its event — acceptable, the mapping refreshes on the next `StartAsync`. If the fetch fails at init (rare, old firmware or weird network), the engine logs `EventStreamSetupFailed` Warning and continues without external detection — normal pushes still work, just without automatic stop on external command until the next session.

**Consumer-side filtering.** The engine ignores events that do not touch a managed resource. In group mode, only `grouped_light` events for the current `_managedGroupId` count — individual `light` events are noise (we don't push per lamp). In multi-light mode, the reverse: only `light` events for a lamp present in `_multiLights` count — `grouped_light` events are noise. The separation avoids double-triggers when a `PUT /groups/{id}/action` naturally generates a group event plus N light events.

**No force-push, no reclaim.** Two alternatives ruled out. (1) Periodic force-push every 2 s even when dedup would have filtered, to overwrite any external modification without an explicit signal — rejected: push war against the user touching their Hue app, useless bridge overload in static regime, hack where the bridge exposes a native event-driven signal. (2) Event-driven reclaim via the SSE — arm a flag and force the next push past dedup to overwrite the external command — rejected in test: complex scenes did not always emit an individual per-lamp event, and the perceptual experience of a lamp flickering 100 ms toward the external state before returning was worse than the lamp staying on the external state. The project doctrine is *fix at the root, don't patch periodically* — and the root here is *honor the user's choice, do not fight them*.

## Security — secrets and sensitive data

The `clientkey` returned by the bridge at pairing is a PSK that will serve the DTLS Entertainment v2 tunnel if ever enabled. It is treated as a secret: never emitted in clear in an EventSource event, never persisted to unencrypted JSON without warning. The `username` (REST application key) is less sensitive but is still truncated to 8 chars + `...` in emissions to minimize exposure in support screenshots. The bridge IP is validated by `IsAcceptableBridgeIp` (RFC1918 + APIPA) to prevent SSRF before the runtime `PUT`.

## Observability

All emissions go through `DeckleLightingSource.Log` — `Deckle.Lighting` provider exposed as a static singleton, LIGHTING tag in the LogWindow. The module is meant to abstract several drivers eventually (WLED, DMX, HomeAssist); the provider is unique for the whole module and future drivers will add their events under the same provider rather than creating a child `Deckle.Lighting.*`.

## Threading and lifetime

`HueBridgeClient` exposes `async Task<...>` methods. Discovery, pairing and control are async on the .NET I/O pool — no UI marshalling needed on the driver side. The consumer (Playground or `AmbientEngine`) calls these methods from any thread, awaiting the result; the UI layer marshals with `DispatcherQueue.TryEnqueue` when it reflects state in XAML controls. `HueRestLightOutput` is `IAsyncDisposable` — closing it releases the internal `HttpClient`; the `username` remains valid on the bridge side as long as the user does not manually revoke it via the Hue app. Retention of the `username` across sessions is a consumer decision (Playground transient, AmbientSettings persistent).

## Pointers

- [src/Deckle.Lighting.Ambient/](../Deckle.Lighting.Ambient/) — main driver consumer, drives the push loop and screen sampling.
- [Philips Hue Developer — Color Conversion Formulas](https://developers.meethue.com/develop/application-design-guidance/color-conversion-formulas-rgb-to-xy-and-back/) — Wide Gamut matrix + Gamut C corners.
- [Björn Ottosson — A Perceptual Color Space for Image Processing](https://bottosson.github.io/posts/oklab/) — OKLab / OKLCh.
