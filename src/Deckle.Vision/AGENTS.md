---
description: Screen capture and frame analysis — DXGI duplication, recovery, HDR.
type: agent-instructions
---

# AGENTS.md — Deckle.Vision

Vision is the sole owner of the `IDXGIOutputDuplication` object: opened at `Start()`, recreated during recovery, released at `Stop()` or loop end.

## Why DXGI Output Duplication, not WGC

`Windows.Graphics.Capture` draws a notification border around the captured surface. Removing it needs `IsBorderRequired=false` plus a `RequestAccessAsync(Borderless)` consent gated by the `graphicsCaptureWithoutBorder` capability, which requires a package identity. Deckle is unpackaged. DXGI Output Duplication has no border. Its constraint: capture must run on the GPU that drives the display.

## Recovery

- Classify a failure before retrying.
- Expected Windows unavailability (secure desktop, lock, UAC, session disconnect) retries indefinitely while `Stop()`'s token is alive.
- An unexpected recreation failure becomes fatal after the initial retries and raises `Stopped`.
- A dead D3D11 device (`DEVICE_REMOVED` / `DEVICE_HUNG`) is immediately fatal. Rebuilding it is the consumer's call.
- Vision supplies the technical cause. The consumer owns the human error and the notification.

## HDR format renegotiation

- Renegotiate the pixel format on every recreate, not only at `Start`, and with a fresh DXGI factory: a factory created before a mode change reports a stale colour space.
- On a format or size change, raise `FormatChanged` so the consumer rebuilds its format-dependent resources. Without it, an HDR↔SDR toggle froze the output silently (see `JOURNAL.md`).

## Threading

The capture loop runs on a dedicated worker thread. `FrameArrived` and `Stopped` are raised from it. Vision knows no dispatcher: consumers marshal themselves.
