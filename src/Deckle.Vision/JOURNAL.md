---
description: Dated diagnostics for Deckle.Vision — the DXGI capture freeze on an HDR toggle and its trail.
type: module-journal
---

# Journal — Deckle.Vision

Dated notes on the capture loop — the why behind a fix the code no longer shows. Most recent on top.

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
