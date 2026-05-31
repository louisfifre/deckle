---
name: claude-deckle-vision
description: "Doctrine for Deckle.Vision, the screen capture and frame analysis module (Windows.Graphics.Capture / DXGI, FrameSampler). Read before touching the DXGI capture loop, the recovery state machine, or the FrameSampler readback path."
type: agent-instructions
module: Deckle.Vision
---

# CLAUDE.md — Deckle.Vision

Screen capture and frame sampling module for the ambient lighting pipeline. Covers `ScreenCaptureService` (DXGI Output Duplication loop, worker threading, recovery on every transient state Windows emits during a session), `FrameSampler` (mip chain + staging + GPU readback to produce a grid of averages consumable by AmbientEngine), and the native interop under `ScreenCaptureInterop`. The module is the sole owner of the `IDXGIOutputDuplication` object; it opens it at `Start()`, silently re-opens it on every transient interruption, and releases it for good only at `Stop()` or on a fatal device error.

## Why DXGI Output Duplication and not Windows.Graphics.Capture

WGC is the modern API but the system draws a yellow border around the captured surface. The only way to disable it is the MSIX `graphicsCaptureWithoutBorder` capability, which cannot be declared from an unpackaged desktop app. DXGI Output Duplication is the pre-WGC API (Windows 8+), not subject to the border, and it is what HyperHDR, OBS, and NVIDIA ShadowPlay use. The full architectural rationale lives in `docs/architecture--color-science-pipeline--0.1.md` axis 2 (the WGC to DXGI migration workstream).

## Recovery — HRESULT taxonomy and retry doctrine

Any long-running DXGI session goes through transient interruptions. The project aligns on the Hyperion.NG pattern (`libsrc/grabber/dda/DDAGrabber.cpp`): *retry forever on transient, surface Stopped only on truly fatal*. The distinction is carried by the HRESULT returned by `AcquireNextFrame` or thrown by `DuplicateOutput1`.

**Transients absorbed silently.** `WAIT_TIMEOUT` (static screen, normal — we continue). `ACCESS_LOST` (desktop switch, mode change, DWM on/off, fullscreen exclusive — we release the duplication and recreate). `ACCESS_DENIED` and `SESSION_DISCONNECTED` (secure desktop: UAC, Win+L lock, screensaver password; RDP disconnect; user switch — same path as ACCESS_LOST, with a dedicated `SecureDesktopRecovering` Verbose log to distinguish the cause). `INVALID_CALL`, `NOT_CURRENTLY_AVAILABLE`, `UNSUPPORTED` (HDR toggle in transit, 4-duplications limit reached, ephemeral 8bpp mode — fall into the generic branch with a 500 ms backoff and retry).

The duplication recreate retries indefinitely as long as the `Stop()` `CancellationToken` has not fired. `TryRecreateDuplication` loops with 2 s between attempts — when `DuplicateOutput1` throws `COMException` (the secure desktop denies access to a non-LOCAL_SYSTEM process, the mode change has not finished propagating, etc.), we log Warning and retry. That is what lets ambient hold without intervention through a screensaver, a multi-minute Win+L, a Run as Administrator command with UAC open. The previous implementation broke on the first failure and fired `Stopped` immediately — from now on `Stopped` fires only on cancel or on a fatal error.

**Fatals.** `DEVICE_REMOVED` and `DEVICE_HUNG` mean the D3D11 device itself is dead (GPU unplugged, driver crash, GPU death signal). The service logs `DeviceLost` and breaks, which fires `Stopped` toward the consumer (`AmbientEngine.OnCaptureStopped`). A full device rebuild would require re-walking adapters/outputs from scratch, which is out of scope for the service — it is the consumer that decides to build a new `ScreenCaptureService` at the next `StartAsync`.

## Threading

The capture loop runs on a dedicated Task spun up in `Start`. `FrameArrived` and `Stopped` are raised from this worker thread, never on the caller's thread. Consumers that touch UI marshal themselves via `DispatcherQueue.TryEnqueue`. The service knows nothing about anyone's dispatcher. The doctrine extends into `AmbientEngine.OnCaptureStopped`, which posts `Stop()` onto the thread pool via `Task.Run` because `Stop()` raises `StateChanged`, which UI subscribers consume.

## HDR and cadence

`DuplicateOutput1` negotiates a pixel format from a priority list — FP16 scRGB preferred when the display is in HDR, BGRA8 preferred otherwise. The retained format is read via `GetDuplicationDesc` and exposed as `ActiveFormat` so that `FrameSampler` chooses its tone-map pass. Peak luminance comes from `IDXGIOutput6::GetDesc1` at adapter enumeration.

The negotiation re-runs on every recreate, not only at `Start`. A mid-session HDR↔SDR desktop toggle invalidates the duplication (`ACCESS_LOST`); `TryRecreateDuplication` re-detects the display's HDR state with a fresh DXGI factory (a factory predating the mode change reports a stale colour space), requests the format list matching the *current* state, reads back the negotiated format, and updates `ActiveFormat`/`PeakLuminance`. When the format or the surface size changed it raises `FormatChanged`, the signal the consumer rebuilds its format-dependent resources on. Without it the recreate recovered the duplication but the pipeline kept tone-mapping the old format into dead output — the silent HDR→SDR freeze fixed 2026-05-31 (see the module `JOURNAL.md`). `AmbientEngine` consumes `FormatChanged` by rebuilding its `FrameSampler` in place on the capture worker thread (serialised against frame delivery, so the swap never races a `Process` call).

The target cadence is ~15 Hz, aligned with the `AmbientEngine` push cadence. Rather than acquiring frames the engine would not consume, we respect the 66 ms window between two actual deliveries — `AcquireNextFrame` keeps running with a 200 ms timeout to stay responsive to cancellation, but we release frames on the GPU without copying into the consumer grid when the window has not elapsed.

## Observability

`DeckleVisionSource.Log` — provider `Deckle.Vision`, tag `VISION` in LogWindow. Session lifecycle (`ScreenCaptureStarting` / `Started` / `Stopped` + Verbose details). Loop anomalies (`AccessLostRecovering`, `SecureDesktopRecovering`, `DeviceLost`, `AcquireFrameFailed`, `TextureQueryFailed`, `FrameConsumerThrew`, `ReleaseFrameNonZero`). Recreate resilience (`DuplicationRecreateAttemptFailed` Warning per failed attempt, `DuplicationRecreated` Verbose on success, `DuplicationResizeDetected` when the display mode changed during the interruption, `CaptureFormatRenegotiated` Info milestone + `CaptureFormatRenegotiatedDetail` Verbose when the recreate landed on a different pixel format — the HDR↔SDR toggle; the Info milestone is ungated so the renegotiation stays visible with the capture gate off). `FrameSampler` covers `SamplerInitialized` + `SamplerMapFailed` + `SamplerProcessFailed`.

The loop Verbose entries are gated by `AmbientCaptureGate` on the Diagnostics side — when the user has `LogAmbientCaptureActivity` off, these lines are filtered before insertion into the LogWindow buffer. Info and Warning always pass through.
