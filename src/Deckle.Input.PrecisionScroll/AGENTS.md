---
description: Precision scrolling domain module — classic wheel detents become native two-finger Precision Touchpad gestures.
type: agent-instructions
---

# AGENTS.md — Deckle.Input.PrecisionScroll

This module converts a notched mouse wheel into system-recognized two-finger Precision Touchpad motion. `Deckle.Input` owns the low-level hook and Win32 injection primitives; this module owns classification, gesture shaping, settings, and the page.

## Doctrine

**Fail open.** Only physical vertical hook events whose delta is exactly `+120` or `-120` are consumed. Injected events, horizontal input, Raw Input observations, and finer deltas continue to Windows unchanged. Creation, injection, or queue failure stops conversion before it can trap the wheel.

**Native gesture, native semantics.** Use the gesture-only `PT_TOUCHPAD` device from `CreateSyntheticPointerDevice2`, physical himetric coordinates, and two complete contacts. Read `SPI_GETTOUCHPADPARAMETERS` once at startup so the wheel keeps its semantic direction when the user reverses touchpad scrolling. Windows owns panning and inertia; Deckle never synthesizes a home-grown scroll curve.

**No work on the hook.** The hook classifies, writes one value to the fixed single-producer/single-consumer ring, signals the worker, and returns. No allocation, lock, I/O, logging, or gesture calculation enters that callback. One background worker owns the synthetic device and emits frames at the native sample's 10 ms cadence through an `AutoResetEvent`, never a UI timer.

**One public magnitude.** The master switch and sensitivity are the whole settings surface. Cadence, contact spacing, frame interval, release delay, backlog response, and rollover stay internal constants until hands-on measurement proves a user-facing need.

## Known boundary

`WH_MOUSE_LL` can suppress input but does not identify its device; Raw Input identifies a device but cannot suppress it. Exact detents are therefore the deliberate discriminator. It matches the measured MX Master stream while leaving the measured high-resolution Precision Touchpad deltas alone, but it cannot distinguish two different classic mice.
