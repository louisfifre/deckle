---
description: Screen-capture and frame-analysis module — DXGI Output Duplication, the transient-recovery doctrine, and HDR format renegotiation.
type: agent-instructions
---

# AGENTS.md — Deckle.Vision

Deckle's screen-side module: capturing the screen (DXGI Output Duplication) and sampling/analyzing frames (`FrameSampler` produces a grid of averages; `IFrameAnalyzer` for richer analysis). Today the ambient lighting consumes the grid, but this is the screen-capture home — capture and analysis together, the visual counterpart to `Deckle.Audio`. The module is the sole owner of the `IDXGIOutputDuplication` object: opened at `Start()`, released and recreated during recovery, and finally released at `Stop()` or loop termination.

## Why DXGI Output Duplication, not WGC

`Windows.Graphics.Capture` is the modern API but draws a system notification border around the captured surface. Removing it isn't a flag: it needs `IsBorderRequired=false` **plus** a runtime consent (`RequestAccessAsync(Borderless)`) gated by the `graphicsCaptureWithoutBorder` capability — which requires a **package identity** an unpackaged app doesn't have. DXGI Output Duplication runs under the compositor, isn't subject to that border, and is the historical path for full-screen capture tools (OBS, HyperHDR, ShadowPlay). Its one constraint: capture must run on the GPU that drives the display (a multi-GPU / Optimus caveat) — fine for a local single screen.

## Recovery — persistent for desktop transitions, bounded for broken ownership

Any long DXGI session hits transient interruptions (static screen, desktop switch, mode change, DWM toggle, secure desktop for UAC / lock / screensaver, RDP). Access loss, denial, and session disconnect are absorbed by releasing and recreating the duplication for as long as `Stop()` has not cancelled the loop. Repeated `DXGI_ERROR_INVALID_CALL` is different: it signals broken frame ownership, so recovery is bounded to ten attempts before the loop raises `Stopped`. A lost or hung D3D11 device also raises `Stopped`; rebuilding the device is the consumer's call. The full HRESULT taxonomy lives in the recovery code.

## HDR format renegotiation

`DuplicateOutput1` negotiates a pixel format (FP16 scRGB when the display is HDR, BGRA8 otherwise), exposed as `ActiveFormat` for `FrameSampler`'s tone-map pass. The negotiation re-runs on **every** recreate, not just at `Start` — and with a **fresh DXGI factory**, because a factory predating a mode change reports a stale colour space. On a format or size change it raises `FormatChanged` so the consumer rebuilds its format-dependent resources; without it, a mid-session HDR↔SDR toggle recovered the duplication but kept tone-mapping the old format into dead output (the silent freeze recorded in the JOURNAL). Cadence is ~15 Hz, aligned with the ambient push — frames outside the 66 ms window are released on the GPU without a copy.

## Threading and observability

The capture loop runs on a dedicated worker thread; `FrameArrived` / `Stopped` are raised from it, never the caller's — the service knows no dispatcher, consumers marshal themselves. Emissions go through `DeckleVisionSource.Log` (VISION tag); the loop's Verbose lines are gated by `AmbientCaptureGate`, Info/Warning always pass.
