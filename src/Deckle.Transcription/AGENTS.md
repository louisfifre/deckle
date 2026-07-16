---
description: Backend-agnostic transcription orchestrator — the IAsrBackend boundary, the model-prime lifecycle, and the clipboard/paste UX rules.
type: agent-instructions
---

# AGENTS.md — Deckle.Transcription

Backend-agnostic voice-transcription orchestrator: hotkey → capture → ASR → optional LLM rewrite → clipboard → optional paste. The ASR implementation lives in a child module behind `IAsrBackend` (`Deckle.Transcription.Whisper` today); the orchestrator never touches P/Invoke, native callbacks, or C structs — a new backend is a new child module, not a change here.

## Model lifecycle

A model is never loaded at boot — nothing sits in VRAM (3 GB+) while idle. It loads and primes on the first hotkey, then unloads after an idle timeout. Capture and the cold prime start concurrently: the HUD enters `"Recording"` with the real audio immediately, while the prime pays model-load and dummy-inference cost in parallel. The first backend call joins that prime through the shared gate; a normal take hides all or most of the cold cost instead of delaying capture.

## Clipboard — two states max

Over one transcription the clipboard holds at most two successive contents: the raw text, then the LLM-rewritten text if a profile is active. Never accumulate token by token — the system clipboard history must stay clean. A future LLM streaming replaces the clipboard object in place; granularity is the sentence, never the token.

## Paste — UI Automation at Stop

Off by default (the HUD shows `Copied to clipboard`). When on: clipboard-safe by default, paste only if UI Automation confirms an editable field (`Edit` or `Document`). Nothing is captured on Start — we trust the system state at Stop, since the user had the whole recording window to place their cursor. A `class name` match misses modern frameworks (Chromium, Electron, Qt…) and must not be reintroduced.
