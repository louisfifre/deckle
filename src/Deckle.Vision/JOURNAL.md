---
description: Dated diagnostics for Deckle.Vision — the DXGI capture freeze on an HDR toggle and its trail.
type: module-journal
---

# Journal — Deckle.Vision

Dated notes on the capture loop — the why behind a fix the code no longer shows. Most recent on top.

## 2026-10-08 — What a 60 Hz capture has to carry along

Found while scoping the move from the 66 ms throttle to ~16 ms (`ScreenCaptureService.ThrottleIntervalMs` is the only cadence knob). Two values in `FrameSampler` are written as 15 Hz facts: the content-peak release is `ContentPeakReleaseDecay = 0.97` applied per processed frame, so the same constant releases four times faster at 60 Hz; the comment on `_contentPeak` calls the one-frame tone-map lag "harmless at 15 Hz". Both need a second look with the cadence. The 2026-10-07 synthesis also records the Ambient smoothing as normalised on 15 Hz; not re-read today.

Also recorded, from the sampler's own comments rather than a new measurement: the CPU side reads a ~30 × 17 grid (~510 pixels, ~1 KB BGRA8 or ~2 KB FP16 per frame) after a GPU mip chain. The capture is not "240p"; the GPU downsample is where the per-frame cost sits, and no cost at 60 Hz has been measured yet. Push rates stay transport-bound (Hue Entertainment at 50 Hz).

## 2026-06-19 — `AcquireNextFrame` INVALID_CALL can wedge Ambient

Found in `app.jsonl`: Ambient can stay running while Vision returns `DXGI_ERROR_INVALID_CALL` from `AcquireNextFrame` every ~500 ms. In that state Ambient heartbeats report `pushed=0` and all ticks dropped; no `ACCESS_LOST`, `ReleaseFrameNonZero`, sampler failure, or consumer exception is emitted first. Microsoft documents this HRESULT as "previous frame not released", so the loop now attempts `ReleaseFrame`, recreates the duplication if DXGI still reports inconsistent ownership, and stops capture after repeated consecutive failures.

## 2026-05-31 — The capture "freeze" on an HDR desktop toggle

Reported symptom: ambient capture stops on its own mid-session. Reproduced — the trigger is the **desktop HDR toggle**, not a game or the secure desktop as first assumed. Diagnosed by reading `app.jsonl` directly.

Measured — two cases by HDR state at capture start:

- Started SDR, HDR switched on → `DuplicateOutput1` returns a transient `E_ACCESSDENIED` on recreate (Warning, visible), then recovers.
- Started HDR, HDR switched off → no log line, capture frozen, no recovery. `attempt` is always 1 on every recreate failure — no retry storm.

In code: `TryRecreateDuplication` re-read the new size but never rewrote the active format, and the `FrameSampler` was built once with a fixed format. Suggested cause of the silent case: `DuplicateOutput1` renegotiates BGRA8 without throwing (so no Warning) while the pipeline still tone-maps FP16 into dead output.

Fixed and confirmed in use: the recreate is now format-aware (fresh DXGI factory, format readback, `FormatChanged` raised) and `AmbientEngine` rebuilds the `FrameSampler` on that signal. Doctrine moved to the CLAUDE.md.

The format hypothesis didn't cover "no recovery even back in HDR" — and that turned out not to be the capture at all: the lingering "freeze" was the delta-gate (static screen → no color change → push suppressed → lamps hold their last value). Intended behavior; the lamps follow as soon as the screen moves.

Left around: `CaptureStallDetector` (an acquire-based watchdog, 7 tests) was written then removed before merge — it modeled an acquire stall never observed, not the format mismatch (acquires keep running; it was the sampler output that died). Git keeps it (`git show e419bf7`) if a capture watchdog is ever wired.

Note: `app.jsonl` persists only the payload — no event name, provider, level, or rendered message — so a param-less event is an empty blob, and with `LogAmbientCaptureActivity` off only Info/Warning/Error pass. Feeds the separate observability workstream.
