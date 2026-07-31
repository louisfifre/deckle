---
description: Precision scrolling domain module — classic wheel detents become native two-finger Precision Touchpad gestures.
type: agent-instructions
---

# AGENTS.md — Deckle.Input.PrecisionScroll

This module converts a notched mouse wheel into system-recognized two-finger Precision Touchpad motion. `Deckle.Input` owns the low-level hook and Win32 injection primitives; this module owns classification, gesture shaping, settings, and the page.

## Doctrine

**Fail open.** Only unmodified physical vertical hook events whose delta is a non-zero multiple of `120` are consumed. This includes Windows-batched classic detents anywhere on the desktop; the synthetic touchpad is mapped through the current mouse location, so Windows routes the gesture to its native target independently of Deckle's focus. Injected events, modified or horizontal input, Raw Input observations, and finer deltas continue to Windows unchanged. Creation or system-direction failure leaves conversion disarmed. Queue overload detaches the interceptor but finishes the already accepted budget; an injection failure is the exceptional irreversible boundary and stops conversion immediately.

**Native gesture, native semantics.** Use the gesture-only `PT_TOUCHPAD` device from `CreateSyntheticPointerDevice2`, physical himetric coordinates, and two complete contacts. Read `SPI_GETTOUCHPADPARAMETERS` once at startup so the wheel keeps its semantic direction when the user reverses touchpad scrolling. Windows owns panning and inertia; Deckle never synthesizes a home-grown scroll curve.

**No work on the hook.** The hook classifies, writes one value to the fixed single-producer/single-consumer ring, signals the worker, and returns. No allocation, lock, I/O, logging, or gesture calculation enters that callback. One background worker owns the synthetic device and emits frames at the native sample's 10 ms cadence through an `AutoResetEvent`, never a UI timer.

**One continuous signal.** Each same-direction detent adds an exact physical-travel budget. Reversal is intentionally immediate: unfinished travel in the old direction is cancelled before the opposite detent begins. The median of up to three observed gaps determines how quickly the current budget is delivered; cadence therefore creates precision or speed without rate filters, acceleration curves, or a locked slow/fast mode. When input stops, the budget reaches zero and a stationary frame prevents native inertia before lift. The full synthetic surface is followed by a native contact rollover when more travel remains.

**Minimal calibration.** The page exposes the complete behavioural model: distance per detent, first-step duration, and release timing. Values apply immediately and share their defaults and bounds with runtime normalization. Distance is the intended user preference; measured use decides whether the other two become fixed constants.

## Known boundary

`WH_MOUSE_LL` can suppress input but does not identify its device; Raw Input identifies a device but cannot suppress it. Exact detents are therefore the deliberate discriminator. It matches the measured MX Master stream while leaving the measured high-resolution Precision Touchpad deltas alone, but it cannot distinguish two different classic mice.
