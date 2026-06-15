---
description: Dated decisions and findings for Deckle.Input — founding choices, measurements, open direction.
type: module-journal
---

# JOURNAL — Deckle.Input

## 2026-06-15 — One mouse per process → shared input host

Mouse Raw Input is one-window-per-process: only the last window registered for the mouse usage page receives WM_INPUT, so two hosts steal the stream from each other. `KeyboardInputHost` became the single per-process keyboard+mouse host, reference-counted by its consumers (autocorrect, wheel capture); the native window and registration come up on the first `Start` and unwind on the last `Stop`. This is the shared input foundation any future mouse consumer must reuse rather than re-register.
