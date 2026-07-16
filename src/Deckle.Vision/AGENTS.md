---
description: Screen-capture and frame-analysis module — DXGI Output Duplication, the transient-recovery doctrine, and HDR format renegotiation.
type: agent-instructions
---

# AGENTS.md — Deckle.Vision

Deckle's screen-side module: capturing the screen (DXGI Output Duplication) and sampling/analyzing frames (`FrameSampler` produces a grid of averages; `IFrameAnalyzer` for richer analysis). Today the ambient lighting consumes the grid, but this is the screen-capture home — capture and analysis together, the visual counterpart to `Deckle.Audio`. The module is the sole owner of the `IDXGIOutputDuplication` object: opened at `Start()`, released and recreated during recovery, and finally released at `Stop()` or loop termination.

## Why DXGI Output Duplication, not WGC

`Windows.Graphics.Capture` is the modern API but draws a system notification border around the captured surface. Removing it isn't a flag: it needs `IsBorderRequired=false` **plus** a runtime consent (`RequestAccessAsync(Borderless)`) gated by the `graphicsCaptureWithoutBorder` capability — which requires a **package identity** an unpackaged app doesn't have. DXGI Output Duplication runs under the compositor, isn't subject to that border, and is the historical path for full-screen capture tools (OBS, HyperHDR, ShadowPlay). Its one constraint: capture must run on the GPU that drives the display (a multi-GPU / Optimus caveat) — fine for a local single screen.

## Recovery — retry forever on expected unavailability, stop on fatal

Any long DXGI session hits interruptions (static screen, desktop switch, mode change, DWM toggle, secure desktop for UAC / lock / screensaver, RDP). Classify before retrying. Expected Windows unavailability keeps retrying indefinitely while `Stop()`'s token has not fired: every 2 s through attempt five, then every 5 s, with an immediate attempt on a Windows unlock signal. This preserves Ambient through a multi-minute Win+L or open UAC prompt without continuously rebuilding DXGI state. Unexpected recreation failures follow the same initial retries but become fatal on attempt five and surface `Stopped`. A dead D3D11 device (`DEVICE_REMOVED` / `DEVICE_HUNG`) remains immediately fatal; rebuilding it is the consumer's call. The workflow consumer owns the human terminal error and notification; Vision supplies the technical cause. The full HRESULT taxonomy lives in the recovery code.

Recovery logging is incident-shaped rather than attempt-shaped. The first failed recreation is `Verbose`; the second opens one `Warning`; later attempts only increment the incident. The first successful recreation emits one `Informational` recovery with a `Verbose` duration/attempt summary. An unexpected fifth failure supplies the terminal technical detail and stops; the workflow owner emits the single human `Error`. Expected unavailability never escalates merely because the user stayed away.

Per-frame texture, readback, sampling, and consumer failures are causes of one processing incident when Vision serves Ambient. Individual failures stay `Verbose`; one second of actual failed processing opens one `Warning`; normal static-screen silence does not count. Five further seconds without a successful analysed frame are terminal for that Ambient run. Vision supplies the technical detail, while Ambient owns the single human `Error`, stop, and persistent user notification.

## HDR format renegotiation

`DuplicateOutput1` negotiates a pixel format (FP16 scRGB when the display is HDR, BGRA8 otherwise), exposed as `ActiveFormat` for `FrameSampler`'s tone-map pass. The negotiation re-runs on **every** recreate, not just at `Start` — and with a **fresh DXGI factory**, because a factory predating a mode change reports a stale colour space. On a format or size change it raises `FormatChanged` so the consumer rebuilds its format-dependent resources; without it, a mid-session HDR↔SDR toggle recovered the duplication but kept tone-mapping the old format into dead output (the silent freeze recorded in the JOURNAL). Cadence is ~15 Hz, aligned with the ambient push — frames outside the 66 ms window are released on the GPU without a copy.

## Threading and observability

The capture loop runs on a dedicated worker thread; `FrameArrived` / `Stopped` are raised from it, never the caller's — the service knows no dispatcher, consumers marshal themselves. Emissions go through `DeckleVisionSource.Log` (VISION tag). When Vision participates in Ambient, its log-only loop details obey Ambient's producer-side verbosity policy; successful capture milestones and fatal technical causes remain mirrors while Ambient owns the visible workflow lifecycle and terminal error. Warnings and errors are never silenced by that policy.
