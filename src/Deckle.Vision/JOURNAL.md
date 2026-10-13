---
description: Dated diagnostics for Deckle.Vision — the DXGI capture freeze on an HDR toggle and its trail.
---

# Journal — Deckle.Vision

Dated notes on the capture loop — the why behind a fix the code no longer shows. Most recent on top.

## 2026-10-08 — Capture at 60 Hz, rate chosen at start

Chose: the fixed 66 ms throttle is gone. `ScreenCaptureService` takes its target rate as a constructor parameter (default 60 Hz); Ambient feeds it from `AmbientSettings.CaptureFrameRateHz`, a closed 60 / 30 / 15 choice snapped by `AmbientCaptureRate`, applied at the next engine start.

Found while making the switch: the old throttle released any frame arriving less than one interval after the last delivered one. At a 16.7 ms interval, a 60 Hz desktop frame arriving slightly early would be dropped, and a 144 Hz desktop would be cut to every third frame (48 Hz). Chose `CapturePacer`: a deadline advanced by one interval per delivery, with a tolerance of one eighth of the interval, restarted from the current frame after a long gap so no burst follows a static screen.

Chose: the HDR content-peak release is a per-second retention (0.97^15 ≈ 0.633, half-life about 1.5 s) applied over the measured interval between processed frames, from `CapturedFrame.TimestampTicks`. At 15 Hz it equals the former 0.97 per frame; the first frame of a sampler applies no release. The one-frame tone-map lag is now ~16 ms.

Unchanged: the push to the lights. The engine push loop reads `FrameSampler.LatestSample` at the transport's rate (Hue Entertainment 50 Hz, REST 15 / 10 Hz), and the smoothing is defined per push tick, so the capture rate does not change the EMA timing. `AcquireTimeoutMs = 200` stays: it bounds the wait on a static screen and Stop latency, not the delivery rate.

Not measured: the cost of 60 Hz against 15 Hz (Vision heartbeat: acquired / dropped, p50 / p95 sample µs). The rate setting allows the comparison without a rebuild.

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
