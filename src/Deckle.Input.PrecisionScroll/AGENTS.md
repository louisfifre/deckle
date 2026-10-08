---
description: Precision scrolling — wheel detents become native touchpad gestures.
type: agent-instructions
---

# AGENTS.md — Deckle.Input.PrecisionScroll

## Doctrine

- **Fail open.** Consume only unmodified physical vertical hook events whose delta is a non-zero multiple of `120`; everything else passes unchanged. A creation failure leaves conversion disarmed. Queue overload detaches the interceptor but finishes the accepted budget. An injection failure stops conversion at once.
- **Native gesture, native semantics.** Use the gesture-only `PT_TOUCHPAD` device from `CreateSyntheticPointerDevice2`. Read `SPI_GETTOUCHPADPARAMETERS` once at startup so the wheel keeps its direction when touchpad scrolling is reversed. Windows owns panning and inertia.
- **No work on the hook.** The hook classifies, writes one value to the ring, signals the worker and returns: no allocation, lock, I/O, logging or math. One worker owns the synthetic device and waits on an `AutoResetEvent`, never a UI timer.
- **One continuous signal.** Reversal cancels unfinished travel. Detent cadence alone sets delivery speed: no rate filters, acceleration curves or speed modes.
- **Minimal calibration.** Three knobs: distance per detent, first-step duration, release timing. Measured use decides whether the last two become constants.

## Known boundary

`WH_MOUSE_LL` can suppress input but not identify its device; Raw Input can identify but not suppress. Exact detents are therefore the discriminator. It cannot tell two classic mice apart.
