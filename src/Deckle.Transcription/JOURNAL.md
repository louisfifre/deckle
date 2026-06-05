---
description: Diagnosis notes and kept decisions for Deckle.Transcription — read on demand when chasing why something is the way it is, not on every visit.
type: module-journal
---

# JOURNAL — Deckle.Transcription

Not read by default. Come here when you need the *why* behind a choice that the code no longer shows.

## 2026-06-05 — Why the model prime is synchronous on the worker thread

The earlier model warmup ran at boot on its own detached thread. It raced a real hotkey transcription: when the user dictated while the warmup inference was still running, priming text occasionally leaked to the clipboard. A `t_isWarmup` `ThreadStatic` flag was used to gate the warmup's user-facing tail (clipboard, rewrite, paste, status events) so it wouldn't surface — fragile, and it didn't close the race.

The fix moved priming onto the worker thread, synchronously, ahead of recording (`EnsurePrimed` at the top of `WorkerRun`). Synchronous-on-worker removes the race structurally — there is no second thread to collide with. The prime now also bypasses the pipeline entirely: it calls `IAsrBackend.TranscribeAsync` directly with an empty segment sink, so there is no user-facing tail to suppress and nothing to gate. `t_isWarmup` was removed as unnecessary.

Not to be confused with the former HUD *composition* warm (`PrimeAndHide` in `Deckle.App` / `Deckle.Hud`) — a boot-time hidden window show, since removed. The model prime described here is the only warmup left on the first-hotkey path.
