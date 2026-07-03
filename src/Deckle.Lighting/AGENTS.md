---
description: Generalist light-output driver — the ILightOutput abstraction and the REST Hue implementation that talks to the lamps. Parent of the lighting consumers.
type: agent-instructions
---

# AGENTS.md — Deckle.Lighting

The generalist driver layer for external lamps: a driver-agnostic `ILightOutput` abstraction plus everything needed to *talk to the lamps* — discovery, pairing, control, color math, the bridge event flow. It knows lamps, not what to show on them. The source and the intent come from the consumers: `Deckle.Lighting.Ambient` drives the lamps from screen capture, a future informative module will drive them from PC signals (load times, status). Both depend on this parent for the driver and the communication; each adds its own specifics on top.

Today's only shipped family is Philips Hue. The low-friction path is still CLIP v1 REST; Hue Entertainment v2 is the high-cadence path for Ambient-style streaming and stays inside the same Hue driver boundary.

## Hue transports

REST CLIP v1 is retained as the compatibility fallback. Hue Entertainment v2 is preferred when an entertainment area and the DTLS `clientkey` are available. The parent module owns both transports behind `ILightOutput` / `IMultiLightOutput`; consumers such as `Deckle.Lighting.Ambient` must not know which Hue transport is active.

Entertainment v2 uses BouncyCastle's managed DTLS-PSK implementation. That third-party dependency is accepted only inside `Deckle.Lighting`'s Hue transport layer; it must not leak into Ambient or future lighting consumers.

## Security

The `clientkey` (DTLS PSK) is never emitted in clear and is persisted only in the shared DPAPI-backed `Deckle.Security` vault; the `username` is truncated to 8 chars in emissions. The bridge IP is validated (RFC1918 + APIPA) at `HueBridgeClient` construction — every path that builds a client (pairing, restore-from-settings, discovery, the ambient push loop) rejects a non-private address before any network call, to prevent SSRF. SubjectPublicKeyInfo pinning of the bridge certificate is a documented future milestone.
