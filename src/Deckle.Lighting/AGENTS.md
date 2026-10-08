---
description: Light-output driver — ILightOutput and the Philips Hue transports.
type: agent-instructions
---

# AGENTS.md — Deckle.Lighting

- This module knows lamps, not what to show on them. The source and intent come from consumers.
- Both Hue transports sit behind `ILightOutput` / `IMultiLightOutput`. Consumers never know which one is active.
- A bridge ID is identity; its IP and port are locators. Endpoint migration needs exactly one discovered candidate whose identity and stored credentials both validate.
- BouncyCastle stays inside the Hue transport layer of this module.
- The `clientkey` is never emitted and is stored only in the DPAPI vault of `Deckle.Security`. The `username` is truncated in emissions.
- `HueBridgeClient` validates the bridge IP (RFC1918 and APIPA) at construction, on every path, before any network call, to prevent SSRF.
- Certificate pinning is future work.
