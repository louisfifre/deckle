---
description: Generalist light-output driver — the ILightOutput abstraction and the REST Hue implementation that talks to the lamps. Parent of the lighting consumers.
type: agent-instructions
---

# CLAUDE.md — Deckle.Lighting

The generalist driver layer for external lamps: a driver-agnostic `ILightOutput` abstraction plus everything needed to *talk to the lamps* — discovery, pairing, control, color math, the bridge event flow. It knows lamps, not what to show on them. The source and the intent come from the consumers: `Deckle.Lighting.Ambient` drives the lamps from screen capture, a future informative module will drive them from PC signals (load times, status). Both depend on this parent for the driver and the communication; each adds its own specifics on top.

Today's only driver is Philips Hue over REST. The driver is stateless beyond its `HttpClient` and its `username`.

## REST CLIP v1, not Entertainment v2

Deliberate: REST CLIP v1 (~10–20 Hz), not the Entertainment v2 DTLS-PSK path — 100% C#, zero native, zero third-party NuGet, enough for a smoothed ambient mode. Entertainment v2 stays archived behind `ILightOutput`; the `clientkey` PSK captured at pairing is kept for it and treated as a secret.

## Security

The `clientkey` (DTLS PSK) is never emitted in clear nor persisted to plain JSON without warning; the `username` is truncated to 8 chars in emissions. The bridge IP is validated (RFC1918 + APIPA) before any `PUT`, to prevent SSRF.
